using System.Globalization;

namespace Lyricod.Core;

[Flags]
public enum KeyModifiers : uint { None = 0, Alt = 1, Control = 2, Shift = 4, Win = 8 }

public enum ShortcutAction { PlayPause, Next, Previous, ToggleDesktop, ToggleIsland, ToggleLyricsWindow, LyricsEarlier, LyricsLater }

/// <summary>Uses Win32 key numbers so saved shortcuts don't depend on Windows assemblies.</summary>
public readonly record struct KeyCombo(KeyModifiers Modifiers, uint VirtualKey)
{
    public static bool IsModifier(uint key) => key is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5;

    public bool IsValid => ((uint)Modifiers & ~15u) == 0 && VirtualKey is >= 8 and <= 254 && !IsModifier(VirtualKey);

    /// <summary>Windows reserves F12 for the debugger, with or without modifiers.</summary>
    public bool IsReserved => VirtualKey == 0x7B;

    /// <summary>Ordinary typing and the debugger key must remain available in every app.</summary>
    public bool IsUsable => IsValid && !IsReserved && ((Modifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Win)) != 0
        || VirtualKey is >= 0x70 and <= 0x87);

    public string Stored => FormattableString.Invariant($"{(uint)Modifiers}:{VirtualKey}");

    public static bool TryParse(string? stored, out KeyCombo combo)
    {
        combo = default;
        var parts = stored?.Split(':');
        if (parts is not { Length: 2 }
            || !uint.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var modifiers)
            || !uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var key)) return false;
        var parsed = new KeyCombo((KeyModifiers)modifiers, key);
        if (!parsed.IsValid) return false;
        combo = parsed;
        return true;
    }

    /// <summary>The app supplies translated key labels; letters and function keys need none.</summary>
    public string Display(Func<string, string> label)
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(KeyModifiers.Control)) parts.Add(label("KeyCtrl"));
        if (Modifiers.HasFlag(KeyModifiers.Alt)) parts.Add(label("KeyAlt"));
        if (Modifiers.HasFlag(KeyModifiers.Win)) parts.Add(label("KeyWin"));
        if (Modifiers.HasFlag(KeyModifiers.Shift)) parts.Add(label("KeyShift"));
        string key;
        if (VirtualKey is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A) key = ((char)VirtualKey).ToString();
        else if (VirtualKey is >= 0x70 and <= 0x87) key = $"F{VirtualKey - 0x6F}";
        else if (VirtualKey is >= 0x60 and <= 0x69) key = label("KeyNumpad") + (VirtualKey - 0x60).ToString(CultureInfo.InvariantCulture);
        else key = VirtualKey switch
        {
            8 => label("KeyBackspace"), 9 => label("KeyTab"), 13 => label("KeyEnter"),
            19 => label("KeyPause"), 20 => label("KeyCapsLock"), 27 => label("KeyEsc"), 32 => label("KeySpace"),
            33 => label("KeyPageUp"), 34 => label("KeyPageDown"), 35 => label("KeyEnd"), 36 => label("KeyHome"),
            37 => "←", 38 => "↑", 39 => "→", 40 => "↓", 45 => label("KeyInsert"), 46 => label("KeyDelete"),
            106 => label("KeyNumpad") + "*", 107 => label("KeyNumpad") + "+", 109 => label("KeyNumpad") + "-",
            110 => label("KeyNumpad") + ".", 111 => label("KeyNumpad") + "/",
            186 => ";", 187 => "=", 188 => ",", 189 => "-", 190 => ".", 191 => "/", 192 => "`",
            219 => "[", 220 => "\\", 221 => "]", 222 => "'",
            _ => $"#{VirtualKey}",
        };
        parts.Add(key);
        return string.Join("+", parts);
    }
}
