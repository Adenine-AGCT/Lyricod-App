using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Lyricod.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Lyricod;

public sealed partial class SettingsWindow
{
    Style Style(string key) => Root.Resources.TryGetValue(key, out var local) ? (Style)local : (Style)Application.Current.Resources[key];

    void Refresh()
    {
        foreach (var refresh in _songWatchers) refresh();
    }

    void Heading(string text) => Page.Children.Add(new TextBlock { Text = text, Style = Style("Heading") });

    void Note(string text) => Page.Children.Add(new TextBlock { Text = text, Style = Style("Note") });

    TextBlock Value() => new() { Style = Style("Detail"), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 420, TextWrapping = TextWrapping.NoWrap };

    static T Centred<T>(T element) where T : FrameworkElement
    {
        element.HorizontalAlignment = HorizontalAlignment.Center;
        return element;
    }

    static StackPanel Row(params UIElement[] items)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        foreach (var item in items) row.Children.Add(item);
        return row;
    }

    /// <summary>A setting: its name (and a line about it) on the left, its control on the right.</summary>
    /// <returns>The line about it, for a page that changes it later; null without one.</returns>
    TextBlock? Card(string title, string? detail, FrameworkElement? control)
    {
        var grid = new Grid { ColumnSpacing = 24 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        text.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
        var line = detail == null ? null : new TextBlock { Text = detail, Style = Style("Detail") };
        if (line != null) text.Children.Add(line);
        grid.Children.Add(text);
        if (control != null)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
        }
        var card = new Border { Style = Style("Card"), Child = grid };
        if (_folding != null)
        {
            // In a fold, plain rows: the fold is the card.
            card.Background = new SolidColorBrush(Colors.Transparent);
            card.BorderThickness = new Thickness(0);
        }
        (_folding ?? Page).Children.Add(card);
        return line;
    }

    /// <summary>The fold being filled, while <see cref="Fold"/> runs.</summary>
    Panel? _folding;

    /// <summary>
    /// Settings folded under one line, out of the way until it's opened. The cards
    /// <paramref name="fill"/> adds go inside. Returns the fold, for a page to hide when it doesn't apply.
    /// </summary>
    Expander Fold(string title, string? detail, Action fill)
    {
        var header = new StackPanel { Spacing = 1, Padding = new Thickness(0, 10, 0, 10) };
        header.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
        if (detail != null) header.Children.Add(new TextBlock { Text = detail, Style = Style("Detail") });
        var inside = new StackPanel();
        var fold = new Expander
        {
            Header = header, Content = inside, Margin = new Thickness(0, 0, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        Page.Children.Add(fold);
        var outer = _folding;
        _folding = inside;
        try { fill(); }
        finally { _folding = outer; }
        return fold;
    }

    /// <summary>Several rows in one card (About).</summary>
    void Group(params UIElement[] rows)
    {
        var inside = new StackPanel { Spacing = 12 };
        foreach (var row in rows) inside.Children.Add(row);
        Page.Children.Add(new Border { Style = Style("Card"), Child = inside, Padding = new Thickness(16, 14, 16, 14) });
    }

    /// <summary>A name, a line about it, and a link out.</summary>
    Grid Entry(string name, string detail, string url)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap, Style = Style("BodyStrongTextBlockStyle") });
        text.Children.Add(new TextBlock { Text = detail, Style = Style("Detail") });
        grid.Children.Add(text);
        var link = new HyperlinkButton { Content = new FontIcon { Glyph = "\uE8A7", FontSize = 14 }, NavigateUri = new Uri(url), VerticalAlignment = VerticalAlignment.Top };
        ToolTipService.SetToolTip(link, url);
        Grid.SetColumn(link, 1);
        grid.Children.Add(link);
        return grid;
    }

    static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception e) when (e is Win32Exception or FileNotFoundException) { }
    }

    ToggleSwitch Toggle(string property, Func<bool> get, Action<bool> set)
    {
        // WinUI's own On/Off would stay in Windows' language.
        var toggle = new ToggleSwitch { IsOn = get(), MinWidth = 0, OnContent = L.Get("ToggleOn"), OffContent = L.Get("ToggleOff") };
        toggle.Toggled += (_, _) => { if (toggle.IsOn != get()) set(toggle.IsOn); };
        Watch(property, () => { if (toggle.IsOn != get()) toggle.IsOn = get(); });
        return toggle;
    }

    ComboBox Choice<T>(string property, Func<T> get, Action<T> set, params (T Value, string Label)[] options)
    {
        var box = new ComboBox { MinWidth = 260 };
        foreach (var option in options) box.Items.Add(option.Label);
        int Index() => Array.FindIndex(options, o => EqualityComparer<T>.Default.Equals(o.Value, get()));
        box.SelectedIndex = Index();
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex >= 0 && box.SelectedIndex != Index()) set(options[box.SelectedIndex].Value);
        };
        Watch(property, () => box.SelectedIndex = Index());
        return box;
    }

    ComboBox Layout(string property, Func<SecondLine> get, Action<SecondLine> set) =>
        Choice(property, get, set, [.. Enum.GetValues<SecondLine>().Select(mode => (mode, L.Get("Layout" + mode)))]);

    /// <summary>A slider for a whole percentage, with its value beside it.</summary>
    FrameworkElement Percent(string property, Func<int> get, Action<int> set, int min, int max)
    {
        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Style = Style("Detail"), MinWidth = 44, TextAlignment = TextAlignment.Right };
        var slider = new Slider { Minimum = min, Maximum = max, StepFrequency = 5, Width = 220, Value = get(), VerticalAlignment = VerticalAlignment.Center };
        void Sync()
        {
            label.Text = $"{get()}%";
            if (Math.Abs(slider.Value - get()) > 0.5) slider.Value = get();
        }
        slider.ValueChanged += (_, e) => set((int)Math.Round(e.NewValue));
        Watch(property, Sync);
        Sync();
        return Row(slider, label);
    }

    FrameworkElement TextSize()
    {
        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Style = Style("Detail"), MinWidth = 44, TextAlignment = TextAlignment.Right };
        var slider = new Slider { Minimum = 18, Maximum = 48, StepFrequency = 1, Width = 220, Value = _settings.DesktopFontSize, VerticalAlignment = VerticalAlignment.Center };
        void Sync()
        {
            label.Text = L.Format("Points", _settings.DesktopFontSize);
            if (Math.Abs(slider.Value - _settings.DesktopFontSize) > 0.5) slider.Value = _settings.DesktopFontSize;
        }
        slider.ValueChanged += (_, e) => _settings.DesktopFontSize = Math.Round(e.NewValue);
        Watch(nameof(Settings.DesktopFontSize), Sync);
        Sync();
        return Row(slider, label);
    }

    FrameworkElement Colours() => Colours(nameof(Settings.SungColor), () => _settings.SungColor, v => _settings.SungColor = v);

    FrameworkElement Colours(string property, Func<string> get, Action<string> set)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var swatches = new List<(Button Button, string Hex)>();
        foreach (var hex in PresetColours)
        {
            var swatch = Swatch(DesktopLyricsWindow.ParseColor(hex));
            swatch.Click += (_, _) => set(hex);
            ToolTipService.SetToolTip(swatch, hex);
            swatches.Add((swatch, hex));
            row.Children.Add(swatch);
        }
        // Any colour, from a picker.
        var picker = new ColorPicker
        {
            Color = DesktopLyricsWindow.ParseColor(get()), IsAlphaEnabled = false,
            IsMoreButtonVisible = false, IsColorChannelTextInputVisible = false,
        };
        picker.ColorChanged += (_, e) => set(Hex(e.NewColor));
        var custom = new Button
        {
            Width = 32, Height = 32, Padding = new Thickness(0), CornerRadius = new CornerRadius(16),
            Content = new FontIcon { Glyph = "\uE790", FontSize = 14 }, // Color
            Flyout = new Flyout { Content = picker },
        };
        ToolTipService.SetToolTip(custom, L.Get("CustomColour"));
        row.Children.Add(custom);

        void Sync()
        {
            var current = get().ToUpperInvariant();
            foreach (var (button, hex) in swatches) button.BorderThickness = new Thickness(hex == current ? 3 : 1);
            custom.BorderThickness = new Thickness(PresetColours.Contains(current) ? 1 : 3);
        }
        Watch(property, Sync);
        Sync();
        return row;
    }

    static Button Swatch(Windows.UI.Color colour) => new()
    {
        Width = 32, Height = 32, Padding = new Thickness(0), CornerRadius = new CornerRadius(16),
        Background = new SolidColorBrush(colour), BorderBrush = new SolidColorBrush(Colors.Gray),
        Resources = { ["ButtonBackgroundPointerOver"] = new SolidColorBrush(colour), ["ButtonBackgroundPressed"] = new SolidColorBrush(colour) },
    };

    static string Hex(Windows.UI.Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>A timing offset: −, the value, +, Reset.</summary>
    FrameworkElement Offset(Func<double> get, Action<double> nudge, Action resetOffset, Func<bool> enabled, string? property)
    {
        var value = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 64, TextAlignment = TextAlignment.Center };
        var minus = new Button { Content = new FontIcon { Glyph = "\uE738", FontSize = 12 }, Padding = new Thickness(10, 8, 10, 8) };
        var plus = new Button { Content = new FontIcon { Glyph = "\uE710", FontSize = 12 }, Padding = new Thickness(10, 8, 10, 8) };
        var reset = new Button { Content = L.Get("Reset") };
        ToolTipService.SetToolTip(minus, L.Get("OffsetLater"));
        ToolTipService.SetToolTip(plus, L.Get("OffsetEarlier"));
        minus.Click += (_, _) => nudge(-0.25);
        plus.Click += (_, _) => nudge(0.25);
        reset.Click += (_, _) => resetOffset();
        void Sync()
        {
            bool on = enabled();
            value.Text = get().ToString("+0.00 s;-0.00 s;0.00 s", CultureInfo.CurrentCulture);
            value.Opacity = on ? 1 : 0.5;
            minus.IsEnabled = plus.IsEnabled = on;
            reset.IsEnabled = on && get() != 0;
        }
        if (property != null) Watch(property, Sync); else _songWatchers.Add(Sync);
        Sync();
        return Row(minus, value, plus, reset);
    }
}
