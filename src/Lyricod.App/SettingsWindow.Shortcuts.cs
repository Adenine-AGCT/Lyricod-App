using System.ComponentModel;
using Lyricod.Core;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Lyricod;

public sealed partial class SettingsWindow
{
    static string ShortcutTitle(ShortcutAction action) => L.Get("Shortcut" + action);

    void RefreshShortcuts() => OnSettingChanged(this, new PropertyChangedEventArgs(nameof(Settings.Shortcuts)));

    void Shortcuts()
    {
        Heading(L.Get("ShortcutPlayback"));
        ShortcutRow(ShortcutAction.PlayPause);
        ShortcutRow(ShortcutAction.Next);
        ShortcutRow(ShortcutAction.Previous);
        Heading(L.Get("ShortcutShow"));
        ShortcutRow(ShortcutAction.ToggleDesktop);
        ShortcutRow(ShortcutAction.ToggleIsland);
        ShortcutRow(ShortcutAction.ToggleLyricsWindow);
        Heading(L.Get("TimingHeading"));
        ShortcutRow(ShortcutAction.LyricsEarlier);
        ShortcutRow(ShortcutAction.LyricsLater);
        Note(L.Get("ShortcutTimingNote"));
        Note(L.Get("ShortcutNote"));
    }

    void ShortcutRow(ShortcutAction action)
    {
        var button = new Button { MinWidth = 180, HorizontalAlignment = HorizontalAlignment.Right };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, ShortcutTitle(action));
        var problem = new TextBlock { Style = Style("Detail"), MaxWidth = 280, TextWrapping = TextWrapping.Wrap };
        var control = new StackPanel { Spacing = 4 };
        control.Children.Add(button);
        control.Children.Add(problem);
        void Sync()
        {
            bool recording = _recording == action;
            var combo = _shortcuts.Combo(action);
            button.Content = recording ? L.Get("ShortcutPressKeys") : combo?.Display(L.Get) ?? L.Get("ShortcutRecord");
            problem.Text = recording ? _recordingProblem ?? ""
                : combo is { IsReserved: true } ? L.Get("ShortcutF12Reserved")
                : _shortcuts.Failed(action) ? L.Get("ShortcutUnavailable") : "";
            problem.Visibility = problem.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        button.Click += (_, _) =>
        {
            StopRecording();
            _recording = action;
            _shortcuts.Paused = true;
            button.Focus(FocusState.Programmatic);
            RefreshShortcuts();
        };
        button.LostFocus += (_, _) => { if (_recording == action) StopRecording(); };
        Sync();
        Watch(nameof(Settings.Shortcuts), Sync);
        Card(ShortcutTitle(action), null, control);
    }

    void StopRecording()
    {
        if (_recording == null) return;
        _recording = null;
        _recordingProblem = null;
        _shortcuts.Paused = false;
        RefreshShortcuts();
    }

    void RecordKey(object sender, KeyRoutedEventArgs e)
    {
        if (e.KeyStatus.WasKeyDown && _recordedKeys.Contains(e.OriginalKey)) { e.Handled = true; return; }
        if (_recording is not { } action) return;
        e.Handled = true;
        _recordedKeys.Add(e.OriginalKey);
        if (e.KeyStatus.WasKeyDown) return;
        if (e.OriginalKey == VirtualKey.Escape) { StopRecording(); return; }
        if (e.OriginalKey is VirtualKey.Back or VirtualKey.Delete)
        {
            _shortcuts.Set(action, null);
            StopRecording();
            return;
        }
        uint key = (uint)e.OriginalKey;
        if (KeyCombo.IsModifier(key)) return;
        static bool Down(VirtualKey k) => (InputKeyboardSource.GetKeyStateForCurrentThread(k) & CoreVirtualKeyStates.Down) != 0;
        var modifiers = KeyModifiers.None;
        if (Down(VirtualKey.Control)) modifiers |= KeyModifiers.Control;
        if (Down(VirtualKey.Menu)) modifiers |= KeyModifiers.Alt;
        if (Down(VirtualKey.Shift)) modifiers |= KeyModifiers.Shift;
        if (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows)) modifiers |= KeyModifiers.Win;
        var combo = new KeyCombo(modifiers, key);
        if (combo.IsReserved) _recordingProblem = L.Get("ShortcutF12Reserved");
        else if (!combo.IsUsable) _recordingProblem = L.Get("ShortcutInvalid");
        else if (_shortcuts.ActionUsing(combo, action) is { } other)
            _recordingProblem = L.Format("ShortcutDuplicate", ShortcutTitle(other));
        else
        {
            _shortcuts.Set(action, combo);
            StopRecording();
            return;
        }
        RefreshShortcuts();
    }
}
