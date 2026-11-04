using Lyricod.Core;
using Lyricod.Core.Romanization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Lyricod;

public sealed partial class IslandWindow
{
    Windows.UI.Text.FontWeight LyricWeight => Fonts.Weight(_settings.IslandWeight);

    void Render()
    {
        var lyrics = _model.Lyrics;
        var line = lyrics is { IsSynced: true } ? _model.CurrentLine : null;
        var song = lyrics != null ? Romanizer.SongLanguage(lyrics) : null;
        var under = line != null ? _settings.IslandSecondLine.RowUnder(_model.CurrentIndex, lyrics) : null;
        HasSecondRow = under != null;
        SecondRow.Height = new GridLength(HasSecondRow ? SecondHeight : 0);
        Second.Visibility = HasSecondRow ? Visibility.Visible : Visibility.Collapsed;
        Second.Text = under?.Text ?? "";
        _secondOpacity = under is { IsExtra: false } ? 0.4 : 0.6;
        if (under is { } u)
        {
            Second.FontFamily = Fonts.For(u.Text, song);
            Second.FontWeight = Fonts.SemiboldWeight;
        }
        _secondWidth = under is { } extra ? Measure(extra.Text, Second.FontFamily, Second.FontSize, Second.FontWeight) : 0;

        if (line == null)
        {
            // No line to sing: the song.
            _pieces = [];
            _piece = null;
            _pieceTimer.Stop();
            Lyric.Text = _model.IsSong && _model.Track is { } t ? $"{t.Title} · {t.Artist}" : "";
            Lyric.FontFamily = Fonts.For(Lyric.Text, null);
            Lyric.FontSize = 13 * LayoutScale;
            Lyric.FontWeight = Fonts.SemiboldWeight;
            LyricDim.Opacity = 0.6;
            Lyric.MaxWidth = MaxText;
            Lyric.TextTrimming = TextTrimming.CharacterEllipsis;
            _pieceWidth = Math.Min(Math.Ceiling(MeasureLyric(Lyric.Text)) + 1, MaxText);
            LyricBox.Width = _pieceWidth;
            _transitionPending = false;
            _lineAnimation?.Stop();
            if (LyricBox.RenderTransform is TranslateTransform slide) slide.Y = 0;
            _sweep.Set(0, 1);
            FitText();
        }
        else
        {
            var font = Fonts.For(line.Text, song);
            Lyric.FontFamily = font;
            Lyric.FontSize = 15 * LayoutScale;
            Lyric.FontWeight = LyricWeight;
            LyricDim.Opacity = 0.4;
            Lyric.MaxWidth = double.PositiveInfinity;
            Lyric.TextTrimming = TextTrimming.None;
            _pieces = LinePieces.Split(line, MaxText - 1, MeasureLyric);
            _piece = null;
            ShowPiece();
        }
    }

    void ShowPiece()
    {
        _pieceTimer.Stop();
        if (_pieces.Count == 0) return;
        double now = _model.Position();
        var piece = LinePieces.Current(_pieces, now)!;
        if (!ReferenceEquals(piece, _piece))
        {
            _piece = piece;
            Lyric.Text = piece.Text;
            Lyric.FontSize = 15 * LayoutScale;
            double natural = MeasureLyric(piece.Text);
            // A single unbreakable word can exceed the split limit. Lay it out smaller.
            if (natural + 1 > MaxText) Lyric.FontSize *= (MaxText - 1) / natural;
            _pieceWidth = Math.Min(Math.Ceiling(MeasureLyric(piece.Text)) + 1, MaxText);
            LyricBox.Width = _pieceWidth;
            _lineAnimation?.Stop();
            if (LyricBox.RenderTransform is TranslateTransform slide) slide.Y = 0;
            // The song's first line just appears; each one after comes in with the transition.
            _transitionPending = _lineShown;
            _lineShown = true;
            FitText();
        }
        _sweep.Animate(piece, _pieceWidth, now, _model.IsPlaying, MeasureLyric);
        int index = _pieces.IndexOf(piece);
        if (_model.IsPlaying && index + 1 < _pieces.Count)
        {
            _pieceTimer.Interval = TimeSpan.FromSeconds(Math.Max(_pieces[index + 1].Start - now, 0) + 0.005);
            _pieceTimer.Start();
        }
    }

    /// <summary>The next line comes in as the island's settings ask: at once, sliding up, or fading.</summary>
    void MoveIn()
    {
        var transition = _settings.IslandTransition;
        if (transition == LineTransition.Instant) return;
        var story = new Storyboard();
        var fade = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromSeconds(0.28), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(fade, LyricBox);
        Storyboard.SetTargetProperty(fade, "Opacity");
        story.Children.Add(fade);
        if (transition == LineTransition.Slide)
        {
            if (LyricBox.RenderTransform is not TranslateTransform) LyricBox.RenderTransform = new TranslateTransform();
            var slide = new DoubleAnimation { From = 10 * LayoutScale, To = 0, Duration = TimeSpan.FromSeconds(0.32), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(slide, LyricBox.RenderTransform);
            Storyboard.SetTargetProperty(slide, "Y");
            story.Children.Add(slide);
        }
        _lineAnimation = story;
        story.Begin();
    }
}
