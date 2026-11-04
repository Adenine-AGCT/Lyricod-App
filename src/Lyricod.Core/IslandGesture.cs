namespace Lyricod.Core;

/// <summary>
/// The deliberate move that expands an island set to hide on hover, like pulling down a shade: the
/// pointer sweeps sideways (across the island, along it, or on its way to it), then pulls down (a
/// floating island: up or down) over the island. The pull may start inside and carry on out past its
/// edge. Anything else hides the island, so brushing past never opens the mini player.
/// </summary>
public static class IslandGesture
{
    /// <summary>How far the sweep has to go sideways, in view pixels.</summary>
    public const double MinSweep = 30;

    /// <summary>How far the pull has to go up or down, in view pixels.</summary>
    public const double MinPull = 30;

    /// <param name="path">Where the pointer has been lately, oldest first, ending where it is now (screen pixels).</param>
    /// <param name="island">The island's bounds on screen.</param>
    /// <param name="scale">The display's scale: pixel distances grow with it.</param>
    /// <param name="downOnly">Only a pull down counts (the default island hangs from the screen's top edge, like a shade).</param>
    public static bool IsSweepThenPull(IReadOnlyList<(double X, double Y)> path, (double Left, double Top, double Width, double Height) island,
        double scale = 1, bool downOnly = false)
    {
        if (path.Count < 3) return false;
        double sweep = MinSweep * scale, pull = MinPull * scale, slack = 6 * scale;
        var end = path[^1];
        for (int corner = path.Count - 2; corner >= 1; corner--)
        {
            var c = path[corner];
            // The pull: far enough, the right way, and more up or down than sideways (hands curve)...
            if (downOnly && end.Y < c.Y) continue;
            double pullX = Math.Abs(end.X - c.X), pullY = Math.Abs(end.Y - c.Y);
            if (pullY < pull || pullX > Math.Max(slack, pullY)) continue;
            // ...over the island: it passes through the island's height within its width.
            bool over = false;
            for (int i = corner; i < path.Count - 1 && !over; i++)
                over = Crosses(path[i], path[i + 1], island, slack);
            if (!over) continue;
            // Before it, the sweep: far enough sideways while staying roughly level.
            for (int start = corner - 1; start >= 0; start--)
            {
                double sweepX = Math.Abs(c.X - path[start].X), sweepY = Math.Abs(c.Y - path[start].Y);
                if (sweepX >= sweep && sweepY <= Math.Max(slack, sweepX * 0.5)) return true;
            }
        }
        return false;
    }

    static bool Crosses((double X, double Y) a, (double X, double Y) b,
        (double Left, double Top, double Width, double Height) island, double slack)
    {
        double enter = 0, leave = 1;
        return Clip(a.X, b.X, island.Left - slack, island.Left + island.Width + slack)
            && Clip(a.Y, b.Y, island.Top - slack, island.Top + island.Height + slack);

        bool Clip(double from, double to, double min, double max)
        {
            double delta = to - from;
            if (delta == 0) return from >= min && from <= max;
            double first = (min - from) / delta, last = (max - from) / delta;
            enter = Math.Max(enter, Math.Min(first, last));
            leave = Math.Min(leave, Math.Max(first, last));
            return enter <= leave;
        }
    }

}
