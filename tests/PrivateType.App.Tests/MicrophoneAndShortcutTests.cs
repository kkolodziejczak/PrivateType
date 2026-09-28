using PrivateType.App;
using PrivateType.Core;
using Xunit;

namespace PrivateType.App.Tests;

public sealed class MicrophoneAndShortcutTests
{
    private static readonly HotkeyDefinition Polish = new(1, "pl-PL", 0x52, 0, "Ctrl+Shift+R");

    private static readonly IReadOnlyList<MicrophoneOption> Microphones =
    [
        new("default", "System default"),
        new("wasapi:headset", "Headset Microphone (USB Audio Device With Long Name)"),
        new("wasapi:array-1", "Microphone Array (Realtek Audio)"),
        new("wasapi:array-2", "Microphone Array (Intel Smart Sound)")
    ];

    [Fact]
    public void Maps_a_truncated_legacy_device_name_to_its_unique_endpoint()
    {
        Assert.Equal("wasapi:headset", MicrophoneCatalog.MatchLegacyProductName("Headset Microphone (USB Audio D", Microphones));
    }

    [Fact]
    public void Leaves_ambiguous_or_unknown_legacy_device_names_unmapped()
    {
        Assert.Null(MicrophoneCatalog.MatchLegacyProductName("Microphone Array", Microphones));
        Assert.Null(MicrophoneCatalog.MatchLegacyProductName("Studio Microphone", Microphones));
        Assert.Null(MicrophoneCatalog.MatchLegacyProductName("System", Microphones));
        Assert.Null(MicrophoneCatalog.MatchLegacyProductName("", Microphones));
    }

    [Fact]
    public void Keeps_non_legacy_microphone_ids_unchanged()
    {
        Assert.Equal("wasapi:headset", MicrophoneCatalog.MigrateLegacyId("wasapi:headset", Microphones));
        Assert.Equal("default", MicrophoneCatalog.MigrateLegacyId("default", Microphones));
    }

    [Fact]
    public void Releases_a_held_shortcut_on_its_key_up()
    {
        var tracker = new HeldShortcutTracker();

        Assert.True(tracker.TryPress(Polish));
        Assert.False(tracker.TryPress(Polish));
        Assert.Equal(HeldKeyUpResult.PassThrough, tracker.KeyUp(0x45));
        Assert.Equal(HeldKeyUpResult.Released, tracker.KeyUp(0x52));
        Assert.Null(tracker.Held);
    }

    [Fact]
    public void Watchdog_keeps_a_shortcut_held_while_its_key_is_down()
    {
        var tracker = new HeldShortcutTracker();
        tracker.TryPress(Polish);

        Assert.False(tracker.ReleaseIfPhysicallyUp(_ => true));
        Assert.Same(Polish, tracker.Held);
    }

    [Fact]
    public void Watchdog_releases_a_missed_key_up_and_swallows_the_late_one()
    {
        var tracker = new HeldShortcutTracker();
        tracker.TryPress(Polish);

        Assert.True(tracker.ReleaseIfPhysicallyUp(_ => false));
        Assert.Null(tracker.Held);
        Assert.False(tracker.ReleaseIfPhysicallyUp(_ => false));
        Assert.Equal(HeldKeyUpResult.Swallowed, tracker.KeyUp(0x52));
        Assert.Equal(HeldKeyUpResult.PassThrough, tracker.KeyUp(0x52));
    }
}
