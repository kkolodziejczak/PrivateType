using System.IO;
using PrivateType.App;
using Xunit;

namespace PrivateType.App.Tests;

public sealed class DictationModeTests
{
    private static readonly HotkeyDefinition Polish = new(1, "pl-PL", 0x52, 0, "Ctrl+Shift+R");
    private static readonly HotkeyDefinition English = new(2, "en-US", 0x45, 0, "Ctrl+Shift+E");

    [Fact]
    public void Toggle_starts_on_the_first_press_and_stops_on_the_next()
    {
        var tracker = new HeldShortcutTracker();

        Assert.Equal(ToggleKeyResult.Started, tracker.ToggleKeyDown(Polish));
        Assert.True(tracker.ToggleKeyUp(0x52));
        Assert.Same(Polish, tracker.Held);
        Assert.Equal(ToggleKeyResult.Stopped, tracker.ToggleKeyDown(Polish));
        Assert.True(tracker.ToggleKeyUp(0x52));
        Assert.Null(tracker.Held);
    }

    [Fact]
    public void Toggle_ignores_auto_repeat_while_the_key_stays_down()
    {
        var tracker = new HeldShortcutTracker();

        Assert.Equal(ToggleKeyResult.Started, tracker.ToggleKeyDown(Polish));
        Assert.Equal(ToggleKeyResult.Swallowed, tracker.ToggleKeyDown(Polish));
        Assert.Equal(ToggleKeyResult.Swallowed, tracker.ToggleKeyDown(Polish));
        Assert.Same(Polish, tracker.Held);
    }

    [Fact]
    public void Toggle_ignores_a_different_shortcut_while_dictating()
    {
        var tracker = new HeldShortcutTracker();
        tracker.ToggleKeyDown(Polish);
        tracker.ToggleKeyUp(0x52);

        Assert.Equal(ToggleKeyResult.Swallowed, tracker.ToggleKeyDown(English));
        Assert.True(tracker.ToggleKeyUp(0x45));
        Assert.Same(Polish, tracker.Held);
        Assert.False(tracker.ToggleKeyUp(0x41));
    }

    [Fact]
    public void Paste_sends_ctrl_v_after_releasing_held_shift_and_alt()
    {
        var inputs = ClipboardPasteInjector.PasteInputs(shiftDown: true, altDown: true);

        Assert.Equal(
            [(0x10, true), (0x12, true), (0x11, false), (0x56, false), (0x56, true), (0x11, true)],
            inputs.Select(input => ((int)input.Data.Keyboard.Vk, (input.Data.Keyboard.Flags & 0x0002) != 0)));
        Assert.All(inputs, input => Assert.Equal(NativeMethods.InputKeyboard, input.Type));
        Assert.Equal(4, ClipboardPasteInjector.PasteInputs(shiftDown: false, altDown: false).Length);
    }

    [Fact]
    public void Pasted_text_is_excluded_from_clipboard_history_and_cloud_sync()
    {
        var data = ClipboardPasteInjector.CreatePasteData("przykładowy tekst");

        Assert.True(data.TryGetData<string>(System.Windows.Forms.DataFormats.UnicodeText, out var text));
        Assert.Equal("przykładowy tekst", text);
        Assert.True(data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing"));
        Assert.True(data.TryGetData<MemoryStream>("CanIncludeInClipboardHistory", out var history));
        Assert.Equal(new byte[4], history!.ToArray());
        Assert.True(data.TryGetData<MemoryStream>("CanUploadToCloudClipboard", out var cloud));
        Assert.Equal(new byte[4], cloud!.ToArray());
    }

    [Fact]
    public void Loading_hint_matches_the_shortcut_behavior()
    {
        Assert.Contains("Keep holding", DictationBubble.ModelLoadingHint);
        Assert.DoesNotContain("holding", DictationBubble.ModelLoadingToggleHint);
    }
}
