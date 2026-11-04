using System.Runtime.InteropServices;

namespace Lyricod.Platform;

/// <summary>The few Win32 calls WinUI doesn't wrap.</summary>
static partial class Win32
{
    public const uint WM_HOTKEY = 0x0312;
    public const uint EVENT_MODIFY_STATE = 0x0002;

    [LibraryImport("kernel32.dll", EntryPoint = "OpenEventW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial nint OpenEvent(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, string name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetEvent(nint handle);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);
    public const uint MOD_NOREPEAT = 0x4000;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(nint hwnd, int id);

    public const int LWA_ALPHA = 0x2;
    public const int WM_NCLBUTTONDOWN = 0xA1;
    public const int HTCAPTION = 2;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetLayeredWindowAttributes(nint hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(nint hwnd);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    /// <summary>The pointer, in screen pixels.</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(nint hwnd, ref POINT point);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hwnd, out RECT rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(nint hwnd, out RECT rect);

    /// <summary>How far a window's inside is from each edge of the whole window, in pixels.</summary>
    public static (int Left, int Top, int Right, int Bottom) FrameEdges(nint hwnd)
    {
        GetWindowRect(hwnd, out var outer);
        GetClientRect(hwnd, out var inner);
        var origin = new POINT();
        ClientToScreen(hwnd, ref origin);
        int left = origin.X - outer.Left, top = origin.Y - outer.Top;
        return (left, top, outer.Right - outer.Left - inner.Right - left, outer.Bottom - outer.Top - inner.Bottom - top);
    }

    /// <summary>The size of a window's inside, in pixels.</summary>
    public static (int Width, int Height) ClientSize(nint hwnd)
    {
        GetClientRect(hwnd, out var inner);
        return (inner.Right, inner.Bottom);
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReleaseCapture();

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial nint SendMessage(nint hwnd, int msg, nint wParam, nint lParam);

    const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    const int DWMWA_BORDER_COLOR = 34;
    const int DWMWCP_DONOTROUND = 1;
    const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref uint value, int size);

    /// <summary>No window border or rounded corners: Windows 11 draws a thin outline round
    /// every top-level window, even a borderless transparent one.</summary>
    public static void RemoveFrame(nint hwnd)
    {
        uint none = DWMWA_COLOR_NONE, square = DWMWCP_DONOTROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref none, sizeof(uint));
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref square, sizeof(uint));
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);

    /// <summary>Makes Windows redraw the frame after its style changed.</summary>
    public static void RefreshFrame(nint hwnd) =>
        SetWindowPos(hwnd, 0, 0, 0, 0, 0, 0x0002 | 0x0001 | 0x0004 | 0x0010 | 0x0020); // NOMOVE NOSIZE NOZORDER NOACTIVATE FRAMECHANGED

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    /// <summary>Clips the window to <paramref name="region"/> (window pixels; Windows then owns it), or 0 to stop.</summary>
    [LibraryImport("user32.dll")]
    public static partial int SetWindowRgn(nint hwnd, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    public const uint WDA_NONE = 0, WDA_EXCLUDEFROMCAPTURE = 0x11;

    /// <summary>Hides the window from screen capture (screenshots, recording, sharing), or shows it again.</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowDisplayAffinity(nint hwnd, uint affinity);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint hwnd);

    /// <summary>Window pixels per view pixel (1.0 at 100 % scaling).</summary>
    public static double Scale(nint hwnd) => Math.Max(GetDpiForWindow(hwnd), 96) / 96.0;
}
