using PrivateType.App;
using PrivateType.Core;
using Xunit;

namespace PrivateType.App.Tests;

public sealed class HotkeyAvailabilityTests
{
    [Fact]
    public void Keeps_the_available_language_when_the_other_hotkey_is_already_taken()
    {
        var availability = HotkeyAvailability.FromRegistrationResults([
            new HotkeyRegistrationAttempt(HotkeyCatalog.Polish, true, 0),
            new HotkeyRegistrationAttempt(HotkeyCatalog.English, false, 1409)
        ]);

        Assert.Equal(["pl-PL"], availability.EnabledLocales);
        Assert.Equal(["en-US"], availability.DisabledLocales);
        Assert.True(availability.CanStart);
    }

    [Fact]
    public void Prevents_start_when_no_language_hotkey_can_be_reserved()
    {
        var availability = HotkeyAvailability.FromRegistrationResults([
            new HotkeyRegistrationAttempt(HotkeyCatalog.Polish, false, 1409),
            new HotkeyRegistrationAttempt(HotkeyCatalog.English, false, 1409)
        ]);

        Assert.False(availability.CanStart);
    }

    [Fact]
    public void Uses_the_accepted_language_shortcuts()
    {
        Assert.Equal("Ctrl+Shift+R", HotkeyCatalog.Polish.Label);
        Assert.Equal("pl-PL", HotkeyCatalog.Polish.LocaleCode);
        Assert.Equal("Ctrl+Shift+E", HotkeyCatalog.English.Label);
        Assert.Equal("en-US", HotkeyCatalog.English.LocaleCode);
    }

    [Fact]
    public void Builds_a_distinct_editable_shortcut_for_each_explicit_language()
    {
        var configured = HotkeyCatalog.FromBindings([
            new ShortcutBinding("pl-PL", 0x31),
            new ShortcutBinding("en-US", 0x72)
        ]);

        Assert.Collection(
            configured,
            polish =>
            {
                Assert.Equal("pl-PL", polish.LocaleCode);
                Assert.Equal("Ctrl+Shift+1", polish.Label);
            },
            english =>
            {
                Assert.Equal("en-US", english.LocaleCode);
                Assert.Equal("Ctrl+Shift+F3", english.Label);
            });
    }

    [Fact]
    public void Builds_an_automatic_recognition_shortcut()
    {
        var configured = HotkeyCatalog.FromBindings([
            new ShortcutBinding("auto", 0x41)
        ]);

        Assert.Equal("auto", configured.Single().LocaleCode);
        Assert.Equal("Ctrl+Shift+A", configured.Single().Label);
    }
}
