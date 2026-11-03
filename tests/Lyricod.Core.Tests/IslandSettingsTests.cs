using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lyricod.Core.Tests;

public class IslandSettingsTests
{
    static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public void DefaultsStayTheSame()
    {
        var settings = new Settings();
        Assert.Equal(IslandHover.ByDirection, settings.IslandHover);
        Assert.Equal("#8CD9FF", settings.IslandColor);
        Assert.Equal(settings.SungColor, settings.IslandColor);
        settings.SungColor = "#FFD36E";
        Assert.Equal("#8CD9FF", settings.IslandColor);
    }

    [Theory]
    [InlineData("{}", "#8CD9FF")]
    [InlineData("{\"SungColor\":\"#FFD36E\"}", "#FFD36E")]
    [InlineData("{\"IslandAlbumColour\":false,\"SungColor\":\"#FF8FB1\"}", "#FF8FB1")]
    [InlineData("{\"SungColor\":\"#9BF59B\",\"IslandAlbumColour\":true}", "#9BF59B")]
    public void OldFilesCopyTheDesktopColourOnce(string json, string expected)
    {
        var settings = JsonSerializer.Deserialize<Settings>(json, Json)!;
        Assert.Equal(expected, settings.IslandColor);
        settings.SungColor = "#FFFFFF";
        Assert.Equal(expected, settings.IslandColor);
        var saved = JsonSerializer.Serialize(settings, Json);
        Assert.Equal(expected, JsonSerializer.Deserialize<Settings>(saved, Json)!.IslandColor);
    }

    [Theory]
    [InlineData("{\"IslandColor\":\"#8CD9FF\",\"SungColor\":\"#FFD36E\"}")]
    [InlineData("{\"SungColor\":\"#FFD36E\",\"IslandColor\":\"#8CD9FF\"}")]
    public void SavedIslandColourWinsInEitherPropertyOrder(string json)
    {
        var settings = JsonSerializer.Deserialize<Settings>(json, Json)!;
        Assert.Equal("#8CD9FF", settings.IslandColor);
    }

    [Fact]
    public void IslandColourNotifiesAndRoundTripsIndependently()
    {
        var settings = new Settings();
        var changed = new List<string?>();
        settings.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        settings.IslandColor = "#ABCDEF";
        settings.IslandColor = "#ABCDEF";
        Assert.Equal([nameof(Settings.IslandColor)], changed);
        Assert.Equal("#8CD9FF", settings.SungColor);
        var saved = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings, Json), Json)!;
        Assert.Equal("#ABCDEF", saved.IslandColor);
        Assert.Equal("#8CD9FF", saved.SungColor);
    }

    [Fact]
    public void IslandIsOnAndDockedByDefault()
    {
        var settings = new Settings();
        Assert.True(settings.ShowIsland);
        Assert.Equal(IslandStyle.Default, settings.IslandStyle);
        Assert.True(settings.IslandLocked);
        Assert.Null(settings.IslandX);
        Assert.Null(settings.IslandY);
        // A file saved with the island off keeps it off.
        Assert.False(JsonSerializer.Deserialize<Settings>("{\"ShowIsland\":false}", Json)!.ShowIsland);
    }

    [Fact]
    public void FloatingDisplayPointRoundTripsWithItsEdge()
    {
        var settings = new Settings { IslandX = 1920, IslandY = 1040, IslandDisplayX = 1770, IslandDisplayY = 1023 };
        var saved = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings, Json), Json)!;
        Assert.Equal(1920, saved.IslandX);
        Assert.Equal(1040, saved.IslandY);
        Assert.Equal(1770, saved.IslandDisplayX);
        Assert.Equal(1023, saved.IslandDisplayY);
        var old = JsonSerializer.Deserialize<Settings>("{\"IslandX\":100,\"IslandY\":50}", Json)!;
        Assert.Null(old.IslandDisplayX);
        Assert.Null(old.IslandDisplayY);
    }

    [Fact]
    public void BeingNewIsntSaved()
    {
        // Only a first run's fresh settings are new (and may then pick defaults for the PC).
        Assert.False(new Settings().IsNew);
        Assert.DoesNotContain("IsNew", JsonSerializer.Serialize(new Settings(), Json));
        Assert.False(JsonSerializer.Deserialize<Settings>("{\"IsNew\":true}", Json)!.IsNew);
    }

    [Fact]
    public void GlassIsOffByDefaultAndItsTransparencyStaysInRange()
    {
        var settings = new Settings();
        Assert.Equal(IslandTheme.Classic, settings.IslandTheme);
        Assert.Equal(90, settings.GlassTransparency);
        Assert.Equal(GlassTint.Neutral, settings.GlassTint);
        Assert.True(settings.GlassShine);
        Assert.Equal(GlassEffect.Liquid, settings.GlassEffect); // what makes it Lyricod Glass
        Assert.Equal(GlassEffect.Liquid, JsonSerializer.Deserialize<Settings>("{\"GlassEffect\":9}", Json)!.GlassEffect);
        Assert.Equal(10, settings.LiquidBlur);
        Assert.Equal(50, settings.LiquidRefraction);
        settings.GlassTransparency = 0;
        Assert.Equal(10, settings.GlassTransparency);
        settings.GlassTransparency = 100;
        Assert.Equal(90, settings.GlassTransparency);
        Assert.Equal(90, JsonSerializer.Deserialize<Settings>("{\"GlassTransparency\":120}", Json)!.GlassTransparency);
    }

    [Theory]
    [InlineData("\"Clear\"", GlassEffect.Clear, 0)]
    [InlineData("\"Frosted\"", GlassEffect.Frosted, 1)]
    [InlineData("\"Liquid\"", GlassEffect.Liquid, 2)]
    public void GlassEffectsKeepTheirNamesAndNumbers(string json, GlassEffect expected, int number)
    {
        Assert.Equal(number, (int)expected);
        Assert.Equal(expected, JsonSerializer.Deserialize<Settings>("{\"GlassEffect\":" + json + "}", Json)!.GlassEffect);
        var settings = JsonSerializer.Deserialize<Settings>("{\"LiquidBlur\":150,\"LiquidRefraction\":-5}", Json)!;
        Assert.Equal(100, settings.LiquidBlur);
        Assert.Equal(0, settings.LiquidRefraction);
    }

    [Theory]
    [InlineData("{\"IslandTheme\":\"Classic\"}", IslandTheme.Classic, GlassTint.Neutral)]
    [InlineData("{\"IslandTheme\":\"Glass\",\"GlassTint\":\"Album\"}", IslandTheme.Glass, GlassTint.Album)]
    [InlineData("{\"IslandTheme\":1,\"GlassTint\":1}", IslandTheme.Glass, GlassTint.Album)]
    [InlineData("{\"IslandTheme\":9,\"GlassTint\":9}", IslandTheme.Classic, GlassTint.Neutral)]
    public void ThemeValuesKeepTheirNamesAndNumbers(string json, IslandTheme theme, GlassTint tint)
    {
        var settings = JsonSerializer.Deserialize<Settings>(json, Json)!;
        Assert.Equal(theme, settings.IslandTheme);
        Assert.Equal(tint, settings.GlassTint);
    }

    [Theory]
    [InlineData("\"Default\"", IslandStyle.Default, 0)]
    [InlineData("\"Floating\"", IslandStyle.Floating, 1)]
    public void StyleValuesKeepTheirNamesAndNumbers(string json, IslandStyle expected, int number)
    {
        Assert.Equal(number, (int)expected);
        Assert.Equal(expected, JsonSerializer.Deserialize<Settings>("{\"IslandStyle\":" + json + "}", Json)!.IslandStyle);
        Assert.Equal(IslandStyle.Default, JsonSerializer.Deserialize<Settings>("{\"IslandStyle\":7}", Json)!.IslandStyle);
    }

    [Theory]
    [InlineData("\"Expand\"", IslandHover.Expand, 0)]
    [InlineData("\"Hide\"", IslandHover.Hide, 1)]
    [InlineData("\"Nothing\"", IslandHover.Nothing, 2)]
    [InlineData("\"ByDirection\"", IslandHover.ByDirection, 3)]
    public void HoverValuesKeepTheirNamesAndNumbers(string json, IslandHover expected, int number)
    {
        Assert.Equal(number, (int)expected);
        Assert.Equal(expected, JsonSerializer.Deserialize<Settings>("{\"IslandHover\":" + json + "}", Json)!.IslandHover);
        Assert.Equal(expected, JsonSerializer.Deserialize<Settings>("{\"IslandHover\":" + number + "}", Json)!.IslandHover);
    }
}
