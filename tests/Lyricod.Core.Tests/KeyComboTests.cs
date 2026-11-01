using System.Globalization;

namespace Lyricod.Core.Tests;

public class KeyComboTests
{
    [Theory]
    [InlineData(0, 65, false)]
    [InlineData(4, 65, false)]
    [InlineData(1, 65, true)]
    [InlineData(2, 65, true)]
    [InlineData(8, 65, true)]
    [InlineData(0, 112, true)]
    [InlineData(4, 135, true)]
    [InlineData(0, 111, false)]
    [InlineData(0, 136, false)]
    [InlineData(16, 65, false)]
    [InlineData(2, 17, false)]
    [InlineData(2, 91, false)]
    [InlineData(2, 92, false)]
    [InlineData(2, 160, false)]
    [InlineData(2, 165, false)]
    [InlineData(2, 0, false)]
    [InlineData(2, 255, false)]
    public void UsableProtectsTyping(uint modifiers, uint key, bool expected) =>
        Assert.Equal(expected, new KeyCombo((KeyModifiers)modifiers, key).IsUsable);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2")]
    [InlineData("2:65:1")]
    [InlineData("-1:65")]
    [InlineData("2:4294967296")]
    [InlineData("16:65")]
    [InlineData("16386:65")]
    [InlineData("2:17")]
    [InlineData("2:0")]
    [InlineData("2:255")]
    [InlineData(" 2:65")]
    [InlineData("2:+65")]
    public void RejectsMalformedSettings(string? stored) => Assert.False(KeyCombo.TryParse(stored, out _));

    [Fact]
    public void SavedFormRoundTripsEveryModifierAndKey()
    {
        for (uint modifiers = 0; modifiers <= 15; modifiers++)
            for (uint key = 8; key <= 254; key++)
            {
                var combo = new KeyCombo((KeyModifiers)modifiers, key);
                if (!combo.IsValid) continue;
                Assert.True(KeyCombo.TryParse(combo.Stored, out var parsed));
                Assert.Equal(combo, parsed);
            }
        Assert.Equal("3:76", new KeyCombo(KeyModifiers.Control | KeyModifiers.Alt, 76).Stored);
    }

    static string Label(string key) => key == "KeySpace" ? "空格" : key[3..];

    [Theory]
    [InlineData(3, 76, "Ctrl+Alt+L")]
    [InlineData(12, 120, "Win+Shift+F9")]
    [InlineData(15, 48, "Ctrl+Alt+Win+Shift+0")]
    [InlineData(0, 135, "F24")]
    [InlineData(2, 32, "Ctrl+空格")]
    [InlineData(2, 37, "Ctrl+←")]
    [InlineData(2, 186, "Ctrl+;")]
    [InlineData(2, 96, "Ctrl+Numpad0")]
    [InlineData(2, 105, "Ctrl+Numpad9")]
    [InlineData(2, 107, "Ctrl+Numpad+")]
    [InlineData(2, 254, "Ctrl+#254")]
    public void DisplaysWindowsOrderAndLocalizedNames(uint modifiers, uint key, string expected) =>
        Assert.Equal(expected, new KeyCombo((KeyModifiers)modifiers, key).Display(Label));

    [Fact]
    public void ParsingDoesNotMakeUnsafeSavedKeysUsable()
    {
        Assert.True(KeyCombo.TryParse("4:65", out var combo));
        Assert.False(combo.IsUsable);
    }

    [Fact]
    public void F12CannotBeRegisteredWithAnyModifierButOldBindingsRemainReadable()
    {
        for (uint modifiers = 0; modifiers <= 15; modifiers++)
        {
            var combo = new KeyCombo((KeyModifiers)modifiers, 0x7B);
            Assert.True(combo.IsReserved);
            Assert.False(combo.IsUsable);
            Assert.True(KeyCombo.TryParse(combo.Stored, out var restored));
            Assert.Equal(combo, restored);
            Assert.False(restored.IsUsable);
            Assert.EndsWith("F12", restored.Display(Label));

            // Neighboring function keys keep their existing registration behavior.
            Assert.True(new KeyCombo((KeyModifiers)modifiers, 0x7A).IsUsable);
            Assert.True(new KeyCombo((KeyModifiers)modifiers, 0x7C).IsUsable);
        }
    }

    [Fact]
    public void StorageDoesNotDependOnCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            var combo = new KeyCombo(KeyModifiers.Control, 76);
            Assert.Equal("2:76", combo.Stored);
            Assert.True(KeyCombo.TryParse(combo.Stored, out var parsed));
            Assert.Equal(combo, parsed);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
