using PrivateType.Core;
using Xunit;

namespace PrivateType.Core.Tests;

public sealed class HistoryShortcutSettingsTests : IDisposable
{
    private const int KeyH = 0x48, KeyR = 0x52, KeyV = 0x56, KeyF9 = 0x78;
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"history-shortcut-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Defaults_to_win_shift_v()
    {
        Assert.Equal("Win+Shift+V", PortableSettings.Default.HistoryShortcut.Label);
        Assert.Null(PortableSettingsValidator.Validate(PortableSettings.Default));
    }

    [Theory]
    [InlineData(true, false, false, true, KeyH, "Win+Ctrl+H")]
    [InlineData(false, true, true, false, KeyH, "Alt+Shift+H")]
    [InlineData(true, true, false, false, KeyF9, "Ctrl+Shift+F9")]
    [InlineData(false, false, false, true, KeyV, "Win+V")]
    public void Names_modifiers_in_a_fixed_order(bool control, bool shift, bool alt, bool windows, int key, string label)
    {
        Assert.Equal(label, new KeyChord(control, shift, alt, windows, key).Label);
    }

    [Theory]
    [InlineData(true, false, false, true, KeyH)]
    [InlineData(false, true, true, false, KeyH)]
    [InlineData(true, true, false, false, KeyH)]
    [InlineData(false, true, false, true, KeyF9)]
    public void Accepts_win_or_two_modifiers(bool control, bool shift, bool alt, bool windows, int key)
    {
        Assert.Null(HistoryShortcutRules.Validate(new KeyChord(control, shift, alt, windows, key), ShortcutBinding.Defaults));
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, false, false)]
    public void Refuses_a_single_modifier_that_would_block_typing_or_pasting(bool control, bool shift, bool alt, bool windows)
    {
        var error = HistoryShortcutRules.Validate(new KeyChord(control, shift, alt, windows, KeyV), ShortcutBinding.Defaults);

        Assert.Equal("Use Win, or two of Ctrl, Alt, and Shift, so the recent dictations shortcut doesn't block typing or pasting.", error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Refuses_ctrl_alt_because_it_is_altgr(bool shift)
    {
        var error = HistoryShortcutRules.Validate(new KeyChord(Control: true, Shift: shift, Alt: true, Windows: false, KeyH), ShortcutBinding.Defaults);

        Assert.Equal("Ctrl+Alt is AltGr on many keyboards and types letters such as ą. Choose another recent dictations shortcut.", error);
    }

    [Fact]
    public void Refuses_a_key_that_is_not_a_letter_number_or_function_key()
    {
        var error = HistoryShortcutRules.Validate(new KeyChord(false, true, false, true, 0x20), ShortcutBinding.Defaults);

        Assert.Equal("Use a letter, number, or function key for the recent dictations shortcut.", error);
    }

    [Fact]
    public void Refuses_the_exact_combination_of_a_dictation_shortcut()
    {
        var error = HistoryShortcutRules.Validate(new KeyChord(true, true, false, false, KeyR), ShortcutBinding.Defaults);

        Assert.Equal("Ctrl+Shift+R is already a dictation shortcut.", error);
    }

    [Fact]
    public void Allows_the_dictation_key_with_extra_modifiers_because_only_exact_matches_open_the_list()
    {
        Assert.Null(HistoryShortcutRules.Validate(new KeyChord(true, true, false, true, KeyR), ShortcutBinding.Defaults));
    }

    [Fact]
    public void Saving_a_conflicting_dictation_shortcut_is_refused()
    {
        var settings = PortableSettings.Default with
        {
            HistoryShortcut = new KeyChord(true, true, false, false, KeyH),
            Shortcuts = [new ShortcutBinding(RecognitionLocaleCatalog.English, KeyH)]
        };

        Assert.Equal("Ctrl+Shift+H is already a dictation shortcut.", PortableSettingsValidator.Validate(settings));
    }

    [Fact]
    public void Round_trips_a_custom_shortcut_through_the_settings_file()
    {
        var store = new PortableSettingsStore(directory);
        store.Save(PortableSettings.Default with { HistoryShortcut = new KeyChord(false, true, true, false, KeyH) });

        var result = store.Load();

        Assert.Null(result.Warning);
        Assert.Equal("Alt+Shift+H", result.Settings.HistoryShortcut.Label);
    }

    [Fact]
    public void Settings_saved_before_the_shortcut_existed_use_the_default()
    {
        var result = Load("""{ "SchemaVersion": 2, "MicrophoneId": "default", "Shortcuts": [{ "LocaleCode": "pl-PL", "VirtualKey": 82 }] }""");

        Assert.Null(result.Warning);
        Assert.Equal(KeyChord.DefaultHistory, result.Settings.HistoryShortcut);
    }

    [Fact]
    public void A_damaged_shortcut_is_reset_alone()
    {
        var result = Load("""{ "SchemaVersion": 2, "MicrophoneId": "default", "Shortcuts": [{ "LocaleCode": "pl-PL", "VirtualKey": 82 }], "HistoryShortcut": { "Control": true, "VirtualKey": 86 }, "ReadySoundVolume": 40 }""");

        Assert.Equal(KeyChord.DefaultHistory, result.Settings.HistoryShortcut);
        Assert.Equal(40, result.Settings.ReadySoundVolume);
        Assert.Equal("Some saved settings were invalid and have been reset: recent dictations shortcut.", result.Warning);
    }

    [Theory]
    [InlineData("\"Win+H\"")]
    [InlineData("{ \"Windows\": true, \"VirtualKey\": \"H\" }")]
    [InlineData("[1, 2]")]
    public void A_shortcut_of_the_wrong_shape_is_reset_alone(string shortcut)
    {
        var result = Load($$"""{ "SchemaVersion": 2, "MicrophoneId": "wasapi:test", "Shortcuts": [{ "LocaleCode": "pl-PL", "VirtualKey": 82 }], "HistoryShortcut": {{shortcut}}, "ReadySoundVolume": 40 }""");

        Assert.Equal(KeyChord.DefaultHistory, result.Settings.HistoryShortcut);
        Assert.Equal("wasapi:test", result.Settings.MicrophoneId);
        Assert.Equal(40, result.Settings.ReadySoundVolume);
        Assert.Equal("Some saved settings were invalid and have been reset: recent dictations shortcut.", result.Warning);
    }

    private SettingsLoadResult Load(string json)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "settings.json"), json);
        return new PortableSettingsStore(directory).Load();
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
