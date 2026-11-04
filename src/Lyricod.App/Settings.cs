using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lyricod.Core;

namespace Lyricod;

/// <summary>Default hangs from the top centre of the main display, as it always has; Floating is
/// fully rounded and can be dragged anywhere.</summary>
public enum IslandStyle { Default, Floating }

/// <summary>How the island looks: Classic black, or Lyricod Glass (see-through, with a shine along its edge).</summary>
public enum IslandTheme { Classic, Glass }

/// <summary>The colour of Lyricod Glass: a neutral dark, or the album cover's colour.</summary>
public enum GlassTint { Neutral, Album }

/// <summary>What Lyricod Glass does with what's behind it: shows it clearly, frosts it with Windows'
/// acrylic, or redraws it lightly blurred and bent at the edges.</summary>
public enum GlassEffect { Clear, Frosted, Liquid }

/// <summary>What the island does while the pointer is over it.</summary>
public enum IslandHover { Expand, Hide, Nothing, ByDirection }

/// <summary>Weight of the island's lyric font.</summary>
public enum IslandWeight { Regular, Medium, Semibold, Bold, Heavy }

/// <summary>Layout size of the Windows island, including its mini player.</summary>
public enum IslandSize { Small, Medium, Large, ExtraLarge }

/// <summary>How the sung part of a line shows (the island and the lyrics window).</summary>
public enum SungEffect { Sweep, Glow, Line }

/// <summary>How the island moves from one line to the next.</summary>
public enum LineTransition { Instant, Slide, Fade }

/// <summary>How often the island's bars redraw: smoother costs more battery.</summary>
public enum FrameRate { BatterySaver = 15, Balanced = 30, Smooth = 60 }

/// <summary>What's behind the lyrics in the lyrics window.</summary>
public enum LyricsBackground { Artwork, StillArtwork, Colour, Dark }

/// <summary>Text size in the lyrics window; Automatic grows with the window.</summary>
public enum LyricsTextSize { Automatic, Small, Medium, Large }

/// <summary>
/// The user's settings, saved as JSON next to the lyrics cache. Defaults follow the macOS
/// app. Change notifications arrive on the thread that made the change (the UI thread).
/// </summary>
public sealed class Settings : INotifyPropertyChanged, IJsonOnDeserialized
{
    public static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lyricod");
    static readonly string FilePath = Path.Combine(DataDir, "settings.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public event PropertyChangedEventHandler? PropertyChanged;

    // Desktop strip
    bool _showDesktopLyrics = true;
    bool _desktopLocked = true;
    double _desktopFontSize = 30;
    string _sungColor = "#8CD9FF";
    SecondLine _secondLine = SecondLine.TranslationOrNext;
    int? _desktopX, _desktopY;

    // Lyricod Island
    bool _showIsland = true;
    IslandStyle _islandStyle = IslandStyle.Default;
    bool _islandLocked = true;
    int? _islandX, _islandY, _islandDisplayX, _islandDisplayY;
    IslandTheme _islandTheme = IslandTheme.Classic;
    int _glassTransparency = 90;
    GlassTint _glassTint = GlassTint.Neutral;
    bool _glassShine = true;
    GlassEffect _glassEffect = GlassEffect.Liquid;
    int _liquidBlur = 10, _liquidRefraction = 50;
    // Hide when the pointer comes in, open the mini player on a sweep then a pull down.
    IslandHover _islandHover = IslandHover.ByDirection;
    bool _islandHideWhenPaused = true;
    bool _islandAlbumColour = true;
    string? _islandColor;
    SecondLine _islandSecondLine = SecondLine.None;
    IslandWeight _islandWeight = IslandWeight.Semibold;
    SungEffect _islandEffect = SungEffect.Sweep;
    LineTransition _islandTransition = LineTransition.Instant;
    bool _islandAudioBars = true;
    IslandSize _islandSize = IslandSize.Medium;
    int _islandMaxTextWidth = 460;

    // Lyrics window
    LyricsBackground _lyricsBackground = LyricsBackground.Artwork;
    LyricsTextSize _lyricsTextSize = LyricsTextSize.Automatic;
    bool _showTranslations = true;
    bool _showPronunciation = true;
    bool _lyricsCentered;
    bool _lyricsBlurDistant = true;
    SungEffect _lyricsEffect = SungEffect.Sweep;
    bool _lyricsAutoHide = true;

    bool _checkForUpdatesAutomatically = true;
    DateTimeOffset? _lastUpdateCheck;
    public bool CheckForUpdatesAutomatically { get => _checkForUpdatesAutomatically; set => Set(ref _checkForUpdatesAutomatically, value); }
    public DateTimeOffset? LastUpdateCheck { get => _lastUpdateCheck; set => Set(ref _lastUpdateCheck, value); }

    // General
    FrameRate _frameRate = FrameRate.Balanced;
    string _playerChoice = "auto";
    string _language = "";

    Dictionary<string, string> _shortcuts = [];
    public Dictionary<string, string> Shortcuts { get => _shortcuts; set => Set(ref _shortcuts, value ?? []); }

    // Lyrics
    string _lyricsFont = "";
    public string LyricsFont { get => _lyricsFont; set => Set(ref _lyricsFont, value ?? ""); }

    double _offset;
    bool _showCredits;
    bool _lookUpOtherPlayers = true;
    List<LyricLanguage> _pronunciationLanguages = [LyricLanguage.Japanese, LyricLanguage.Korean];

    public bool ShowDesktopLyrics { get => _showDesktopLyrics; set => Set(ref _showDesktopLyrics, value); }
    /// <summary>Locked: clicks pass through the strip. Unlocked: it can be dragged.</summary>
    public bool DesktopLocked { get => _desktopLocked; set => Set(ref _desktopLocked, value); }
    public double DesktopFontSize { get => _desktopFontSize; set => Set(ref _desktopFontSize, Math.Clamp(value, 18, 48)); }
    /// <summary>"#RRGGBB".</summary>
    public string SungColor { get => _sungColor; set => Set(ref _sungColor, value); }
    public SecondLine SecondLine { get => _secondLine; set => Set(ref _secondLine, value); }
    /// <summary>Where the strip was dragged to (screen pixels), or null for bottom centre.</summary>
    public int? DesktopX { get => _desktopX; set => Set(ref _desktopX, value); }
    public int? DesktopY { get => _desktopY; set => Set(ref _desktopY, value); }

    /// <summary>The pill at the top centre of the screen (or where it floats). On by default.</summary>
    public bool ShowIsland { get => _showIsland; set => Set(ref _showIsland, value); }
    public IslandStyle IslandStyle { get => _islandStyle; set => Set(ref _islandStyle, Enum.IsDefined(value) ? value : IslandStyle.Default); }
    /// <summary>Floating only. Locked: the island works as usual. Unlocked: it takes clicks and can be dragged.</summary>
    public bool IslandLocked { get => _islandLocked; set => Set(ref _islandLocked, value); }
    /// <summary>Where the floating island was dragged to (screen pixels, see <see cref="Core.IslandPlacement"/>),
    /// or null for the top centre.</summary>
    public int? IslandX { get => _islandX; set => Set(ref _islandX, value); }
    public int? IslandY { get => _islandY; set => Set(ref _islandY, value); }
    /// <summary>A point inside the display it was dropped on; the saved edge may be outside it.</summary>
    public int? IslandDisplayX { get => _islandDisplayX; set => Set(ref _islandDisplayX, value); }
    public int? IslandDisplayY { get => _islandDisplayY; set => Set(ref _islandDisplayY, value); }
    public IslandTheme IslandTheme { get => _islandTheme; set => Set(ref _islandTheme, Enum.IsDefined(value) ? value : IslandTheme.Classic); }
    /// <summary>Lyricod Glass: how see-through it is, in percent (10 to 90).</summary>
    public int GlassTransparency { get => _glassTransparency; set => Set(ref _glassTransparency, Math.Clamp(value, 10, 90)); }
    public GlassTint GlassTint { get => _glassTint; set => Set(ref _glassTint, Enum.IsDefined(value) ? value : GlassTint.Neutral); }
    /// <summary>Lyricod Glass's liquid effect: light along the glass's edge, and a shadow under a floating island.</summary>
    public bool GlassShine { get => _glassShine; set => Set(ref _glassShine, value); }
    /// <summary>Lyricod Glass's effect: liquid unless chosen otherwise (it stays clear where Windows can't run it).</summary>
    public GlassEffect GlassEffect { get => _glassEffect; set => Set(ref _glassEffect, Enum.IsDefined(value) ? value : GlassEffect.Liquid); }
    /// <summary>Liquid glass: how much what's behind is blurred, in percent.</summary>
    public int LiquidBlur { get => _liquidBlur; set => Set(ref _liquidBlur, Math.Clamp(value, 0, 100)); }
    /// <summary>Liquid glass: how strongly its edges bend what's behind, in percent.</summary>
    public int LiquidRefraction { get => _liquidRefraction; set => Set(ref _liquidRefraction, Math.Clamp(value, 0, 100)); }
    public IslandHover IslandHover { get => _islandHover; set => Set(ref _islandHover, value); }
    /// <summary>The island goes away a moment after the music stops, and comes back when it plays.</summary>
    public bool IslandHideWhenPaused { get => _islandHideWhenPaused; set => Set(ref _islandHideWhenPaused, value); }
    /// <summary>Tint the island's lyric with the album cover's colour instead of its chosen colour.</summary>
    public bool IslandAlbumColour { get => _islandAlbumColour; set => Set(ref _islandAlbumColour, value); }
    /// <summary>"#RRGGBB", used when album colour is off or unavailable.</summary>
    public string IslandColor { get => _islandColor ?? "#8CD9FF"; set => Set(ref _islandColor, value); }
    public SecondLine IslandSecondLine { get => _islandSecondLine; set => Set(ref _islandSecondLine, value); }
    public IslandWeight IslandWeight { get => _islandWeight; set => Set(ref _islandWeight, value); }
    public SungEffect IslandEffect { get => _islandEffect; set => Set(ref _islandEffect, value); }
    public LineTransition IslandTransition { get => _islandTransition; set => Set(ref _islandTransition, value); }
    /// <summary>The bars follow the player's actual sound (falls back to a steady bob when there's none).</summary>
    public bool IslandAudioBars { get => _islandAudioBars; set => Set(ref _islandAudioBars, value); }
    public IslandSize IslandSize { get => _islandSize; set => Set(ref _islandSize, Enum.IsDefined(value) ? value : IslandSize.Medium); }
    /// <summary>Text width at Medium size, before a long line is split into pieces.</summary>
    public int IslandMaxTextWidth { get => _islandMaxTextWidth; set => Set(ref _islandMaxTextWidth, Math.Clamp(value, 320, 600)); }

    void IJsonOnDeserialized.OnDeserialized()
    {
        // Old files used the desktop colour. Copy it once, after all properties were read.
        _islandColor ??= _sungColor;
    }

    public LyricsBackground LyricsBackground { get => _lyricsBackground; set => Set(ref _lyricsBackground, value); }
    public LyricsTextSize LyricsTextSize { get => _lyricsTextSize; set => Set(ref _lyricsTextSize, value); }
    /// <summary>The lyrics window shows translations / pronunciation under each line (its buttons flip these).</summary>
    public bool ShowTranslations { get => _showTranslations; set => Set(ref _showTranslations, value); }
    public bool ShowPronunciation { get => _showPronunciation; set => Set(ref _showPronunciation, value); }
    public bool LyricsCentered { get => _lyricsCentered; set => Set(ref _lyricsCentered, value); }
    /// <summary>Lines away from the one being sung blur a little more the further they are.</summary>
    public bool LyricsBlurDistant { get => _lyricsBlurDistant; set => Set(ref _lyricsBlurDistant, value); }
    public SungEffect LyricsEffect { get => _lyricsEffect; set => Set(ref _lyricsEffect, value); }
    /// <summary>In wide windows and full screen, controls fade while the pointer rests.</summary>
    public bool LyricsAutoHide { get => _lyricsAutoHide; set => Set(ref _lyricsAutoHide, value); }

    public FrameRate FrameRate { get => _frameRate; set => Set(ref _frameRate, value); }
    /// <summary>"auto" follows whichever player is playing; otherwise a player's app id to always follow.</summary>
    public string PlayerChoice { get => _playerChoice; set => Set(ref _playerChoice, value); }
    /// <summary>"" follows Windows; otherwise a language Lyricod ships ("en-US", "zh-Hans"). Applies after a restart.</summary>
    public string Language { get => _language; set => Set(ref _language, value); }

    /// <summary>Seconds for every song; positive shows lyrics earlier.</summary>
    public double Offset { get => _offset; set => Set(ref _offset, Math.Round(value, 2)); }
    public bool ShowCredits { get => _showCredits; set => Set(ref _showCredits, value); }
    public bool LookUpOtherPlayers { get => _lookUpOtherPlayers; set => Set(ref _lookUpOtherPlayers, value); }
    /// <summary>Languages that get a pronunciation line. Chinese is off by default.</summary>
    public List<LyricLanguage> PronunciationLanguages { get => _pronunciationLanguages; set => Set(ref _pronunciationLanguages, value); }

    void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Save();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public static Settings Load()
    {
        Settings? s = null;
        try
        {
            if (File.Exists(FilePath)) s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        s ??= new Settings { IsNew = true };
        s._ready = true; // reading the file set every property; saving starts now
        return s;
    }

    bool _ready;

    /// <summary>Made fresh, with no saved settings to read (a first run): defaults that depend on the PC can still be chosen.</summary>
    [JsonIgnore] public bool IsNew { get; private set; }

    void Save()
    {
        if (!_ready) return;
        try
        {
            Directory.CreateDirectory(DataDir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    [JsonConstructor]
    public Settings() { }
}
