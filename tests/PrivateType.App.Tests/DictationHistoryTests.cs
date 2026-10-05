using System.IO;
using PrivateType.App;
using PrivateType.Core;
using Xunit;

namespace PrivateType.App.Tests;

public sealed class DictationHistoryTests
{
    private const int VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12, VkLeftWindows = 0x5B, VkRightWindows = 0x5C, VkV = 0x56, VkH = 0x48, VkLeftMenu = 0xA4, VkRightMenu = 0xA5;

    [Theory]
    [InlineData(VkLeftWindows)]
    [InlineData(VkRightWindows)]
    public void Win_shift_v_opens_once_and_swallows_repeats_and_the_key_up(int windowsKey)
    {
        var shortcut = new HistoryShortcut();
        var pressed = Pressed(windowsKey, VkShift);

        Assert.True(shortcut.Handle(HotkeyMessage.KeyDown, VkV, pressed, out var opened));
        Assert.True(opened);
        Assert.True(shortcut.Handle(HotkeyMessage.KeyDown, VkV, pressed, out var repeated));
        Assert.False(repeated);
        Assert.True(shortcut.Handle(HotkeyMessage.KeyUp, VkV, pressed, out _));
        Assert.False(shortcut.Handle(HotkeyMessage.KeyUp, VkV, pressed, out _));
    }

    [Theory]
    [InlineData(VkShift)]
    [InlineData(VkLeftWindows)]
    [InlineData(VkLeftWindows, VkShift, VkControl)]
    [InlineData(VkLeftWindows, VkShift, VkMenu)]
    [InlineData(VkControl, VkShift)]
    public void Other_combinations_with_v_pass_through(params int[] keys)
    {
        var shortcut = new HistoryShortcut();

        Assert.False(shortcut.Handle(HotkeyMessage.KeyDown, VkV, Pressed(keys), out var opened));
        Assert.False(opened);
        Assert.False(shortcut.Handle(HotkeyMessage.KeyUp, VkV, Pressed(keys), out _));
    }

    [Fact]
    public void Other_keys_pass_through_even_with_win_and_shift_held()
    {
        var shortcut = new HistoryShortcut();

        Assert.False(shortcut.Handle(HotkeyMessage.KeyDown, 0x43, Pressed(VkLeftWindows, VkShift), out _));
    }

    [Fact]
    public void Clearing_forgets_a_pressed_v_so_its_late_key_up_passes_through()
    {
        var shortcut = new HistoryShortcut();
        shortcut.Handle(HotkeyMessage.KeyDown, VkV, Pressed(VkLeftWindows, VkShift), out _);

        shortcut.Clear();

        Assert.False(shortcut.Handle(HotkeyMessage.KeyUp, VkV, Pressed(), out _));
    }

    [Theory]
    [InlineData(VkLeftWindows)]
    [InlineData(VkRightWindows)]
    public void Holds_back_only_the_first_win_release_after_opening(int windowsKey)
    {
        var shortcut = new HistoryShortcut();
        Assert.False(shortcut.TakeModifierRelease(HotkeyMessage.KeyUp, windowsKey));

        shortcut.Handle(HotkeyMessage.KeyDown, VkV, Pressed(windowsKey, VkShift), out _);

        Assert.False(shortcut.TakeModifierRelease(HotkeyMessage.KeyUp, VkShift));
        Assert.True(shortcut.TakeModifierRelease(HotkeyMessage.KeyUp, windowsKey));
        Assert.False(shortcut.TakeModifierRelease(HotkeyMessage.KeyUp, windowsKey));
    }

    [Fact]
    public void A_new_win_press_cancels_a_held_back_release_that_was_missed()
    {
        var shortcut = new HistoryShortcut();
        shortcut.Handle(HotkeyMessage.KeyDown, VkV, Pressed(VkLeftWindows, VkShift), out _);

        Assert.False(shortcut.TakeModifierRelease(HotkeyMessage.KeyDown, VkLeftWindows));
        Assert.False(shortcut.TakeModifierRelease(HotkeyMessage.KeyUp, VkLeftWindows));
    }

    [Fact]
    public void A_missed_key_up_does_not_swallow_later_ordinary_v_presses()
    {
        var shortcut = new HistoryShortcut();
        shortcut.Handle(HotkeyMessage.KeyDown, VkV, Pressed(VkLeftWindows, VkShift), out _);

        Assert.False(shortcut.Handle(HotkeyMessage.KeyDown, VkV, Pressed(), out _));
        Assert.False(shortcut.Handle(HotkeyMessage.KeyUp, VkV, Pressed(), out _));
        Assert.True(shortcut.Handle(HotkeyMessage.KeyDown, VkV, Pressed(VkLeftWindows, VkShift), out var reopened));
        Assert.True(reopened);
    }

    [Fact]
    public void A_custom_shortcut_opens_only_on_its_exact_combination()
    {
        var shortcut = new HistoryShortcut { Chord = new KeyChord(Control: false, Shift: true, Alt: true, Windows: false, VkH) };

        Assert.False(shortcut.Handle(HotkeyMessage.KeyDown, VkV, Pressed(VkLeftWindows, VkShift), out _));
        Assert.False(shortcut.Handle(HotkeyMessage.KeyDown, VkH, Pressed(VkMenu, VkShift, VkControl), out _));
        Assert.True(shortcut.Handle(HotkeyMessage.KeyDown, VkH, Pressed(VkMenu, VkShift), out var opened));
        Assert.True(opened);
    }

    [Theory]
    [InlineData(VkMenu)]
    [InlineData(VkLeftMenu)]
    [InlineData(VkRightMenu)]
    public void Holds_back_the_alt_release_so_the_menu_bar_does_not_open(int altKey)
    {
        var shortcut = new HistoryShortcut { Chord = new KeyChord(false, true, true, false, VkH) };
        shortcut.Handle(HotkeyMessage.KeyDown, VkH, Pressed(VkMenu, VkShift), out _);

        Assert.False(shortcut.TakeModifierRelease(HotkeyMessage.KeyUp, VkLeftWindows));
        Assert.True(shortcut.TakeModifierRelease(HotkeyMessage.KeyUp, altKey));
    }

    [Fact]
    public void Ctrl_shift_shortcuts_hold_back_no_release()
    {
        var shortcut = new HistoryShortcut { Chord = new KeyChord(true, true, false, false, VkH) };
        shortcut.Handle(HotkeyMessage.KeyDown, VkH, Pressed(VkControl, VkShift), out _);

        Assert.False(shortcut.TakeModifierRelease(HotkeyMessage.KeyUp, VkMenu));
        Assert.False(shortcut.TakeModifierRelease(HotkeyMessage.KeyUp, VkLeftWindows));
    }

    [Fact]
    public void Win_shift_v_does_not_hold_back_an_alt_release()
    {
        var shortcut = new HistoryShortcut();
        shortcut.Handle(HotkeyMessage.KeyDown, VkV, Pressed(VkLeftWindows, VkShift), out _);

        Assert.False(shortcut.TakeModifierRelease(HotkeyMessage.KeyUp, VkLeftMenu));
    }

    [Fact]
    public void Recorder_captures_a_win_combination_once_and_swallows_its_key()
    {
        var recorder = new KeyChordRecorder();
        var pressed = Pressed(VkLeftWindows, VkShift);

        Assert.False(recorder.Handle(HotkeyMessage.KeyDown, VkLeftWindows, pressed, out _));
        Assert.True(recorder.Handle(HotkeyMessage.KeyDown, VkV, pressed, out var recorded));
        Assert.Equal(KeyChord.DefaultHistory, recorded);
        Assert.True(recorder.Handle(HotkeyMessage.KeyDown, VkV, pressed, out var repeated));
        Assert.Null(repeated);
        Assert.True(recorder.Handle(HotkeyMessage.KeyUp, VkV, pressed, out _));
        Assert.True(recorder.TakeModifierRelease(HotkeyMessage.KeyUp, VkLeftWindows));
    }

    [Theory]
    [InlineData(0x09)]
    [InlineData(0x1B)]
    [InlineData(VkH, VkShift)]
    public void Recorder_lets_plain_and_shift_keys_through_so_tab_and_esc_still_work(int key, params int[] held)
    {
        var recorder = new KeyChordRecorder();

        Assert.False(recorder.Handle(HotkeyMessage.KeyDown, key, Pressed(held), out var recorded));
        Assert.Null(recorded);
    }

    [Fact]
    public void Opting_in_lets_pasted_text_into_clipboard_history_but_never_cloud_clipboard()
    {
        var data = ClipboardPasteInjector.CreatePasteData("przykładowy tekst", includeInClipboardHistory: true);

        Assert.True(data.TryGetData<string>(System.Windows.Forms.DataFormats.UnicodeText, out var text));
        Assert.Equal("przykładowy tekst", text);
        Assert.False(data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing"));
        Assert.False(data.GetDataPresent("CanIncludeInClipboardHistory"));
        Assert.True(data.TryGetData<MemoryStream>("CanUploadToCloudClipboard", out var cloud));
        Assert.Equal(new byte[4], cloud!.ToArray());
    }

    [Fact]
    public void Kept_text_hint_names_the_history_shortcut()
    {
        Assert.Equal("Press Win+Shift+V to paste it.", DictationApplication.KeptTextHint(KeyChord.DefaultHistory));
        Assert.Equal("Press Alt+Shift+H to paste it.", DictationApplication.KeptTextHint(new KeyChord(false, true, true, false, 0x48)));
    }

    [Fact]
    public void Describes_age_language_and_insertion_outcome()
    {
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        var entry = new DictationHistoryEntry(1, "  Ala\nma   kota ", "pl-PL", now.AddMinutes(-3), Inserted: false);

        var item = DictationHistoryItem.From(entry, now, TimeZoneInfo.Utc);

        Assert.Equal("Ala ma kota", item.Preview);
        Assert.Equal("3 min ago · Polish (Poland)", item.Details);
        Assert.True(item.ShowNotInserted);
    }

    [Theory]
    [InlineData(0, "Just now")]
    [InlineData(59, "Just now")]
    [InlineData(60, "1 min ago")]
    [InlineData(59 * 60, "59 min ago")]
    [InlineData(2 * 60 * 60, "10:00")]
    [InlineData(13 * 60 * 60, "1 Oct, 23:00")]
    public void Ages_read_as_relative_minutes_then_clock_time(int secondsAgo, string expected)
    {
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(expected, DictationHistoryItem.Age(now.AddSeconds(-secondsAgo), now, TimeZoneInfo.Utc));
    }

    private const nint PreviousApp = 100, List = 200, OtherApp = 300;

    [Fact]
    public void List_that_never_got_the_foreground_stays_open_while_the_previous_app_keeps_it()
    {
        var dismissal = new HistoryDismissal(PreviousApp);

        Assert.False(dismissal.ShouldClose(PreviousApp, List));
        Assert.False(dismissal.ShouldClose(nint.Zero, List));
        Assert.False(dismissal.HeldForeground);
    }

    [Fact]
    public void List_closes_when_it_loses_the_foreground_it_held()
    {
        var dismissal = new HistoryDismissal(PreviousApp);

        Assert.False(dismissal.ShouldClose(List, List));
        Assert.True(dismissal.HeldForeground);
        Assert.False(dismissal.ShouldClose(nint.Zero, List));
        Assert.True(dismissal.ShouldClose(PreviousApp, List));
    }

    [Fact]
    public void List_closes_when_another_app_takes_the_foreground_even_if_it_never_held_it()
    {
        var dismissal = new HistoryDismissal(PreviousApp);

        Assert.True(dismissal.ShouldClose(OtherApp, List));
    }

    [Fact]
    public void List_opened_without_a_foreground_window_waits_on_the_first_app_that_takes_it()
    {
        var dismissal = new HistoryDismissal(nint.Zero);

        Assert.False(dismissal.ShouldClose(PreviousApp, List));
        Assert.False(dismissal.ShouldClose(PreviousApp, List));
        Assert.True(dismissal.ShouldClose(OtherApp, List));
    }

    private static Func<int, bool> Pressed(params int[] keys) => key => keys.Contains(key);
}
