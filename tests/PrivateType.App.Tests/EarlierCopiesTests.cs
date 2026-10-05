using System.IO;
using PrivateType.App;
using PrivateType.Core;
using Xunit;

namespace PrivateType.App.Tests;

public sealed class EarlierCopiesTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"earlier-copies-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Finds_the_most_recently_used_release_beside_this_one()
    {
        var current = Release("PrivateType 1.4.0", withSettings: false);
        var older = Release("PrivateType 1.2.0", lastUsed: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        var newer = Release("PrivateType 1.3.0", lastUsed: new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));

        var found = EarlierCopyFinder.FindMostRecent(current, []);

        Assert.Equal(newer, found?.AppDirectory);
        Assert.Equal(Path.Combine(root, "PrivateType 1.3.0"), found?.Folder);
        Assert.NotEqual(older, found?.AppDirectory);
    }

    [Fact]
    public void Copies_are_ranked_by_their_last_settings_change()
    {
        var current = Release(Path.Combine("new", "PrivateType 1.5.0"), withSettings: false);
        var startup = Release(Path.Combine("a", "PrivateType 1.2.0"), lastUsed: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        var sibling = Release(Path.Combine("new", "PrivateType 1.3.0"), lastUsed: new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(sibling, EarlierCopyFinder.FindMostRecent(current, [startup])?.AppDirectory);
    }

    [Fact]
    public void Finds_a_release_among_many_unrelated_folders()
    {
        var current = Release("PrivateType 1.4.0", withSettings: false);
        foreach (var index in Enumerable.Range(0, 230))
            Directory.CreateDirectory(Path.Combine(root, $"folder {index:D3}"));
        var older = Release("zz PrivateType 1.3.0");

        Assert.Equal(older, EarlierCopyFinder.FindMostRecent(current, [])?.AppDirectory);
    }

    [Fact]
    public void Refuses_to_import_settings_that_cannot_be_read()
    {
        var app = Release("PrivateType 1.3.0");
        File.WriteAllText(Path.Combine(app, "data", "settings.json"), "{ broken");

        Assert.Throws<IOException>(() => SettingsImport.Read(EarlierCopyFinder.FromChosenFolder(app)!, startWithWindows: false));
    }

    [Fact]
    public void Passes_on_the_warning_for_settings_repaired_in_the_earlier_copy()
    {
        var app = Release("PrivateType 1.3.0");
        File.WriteAllText(Path.Combine(app, "data", "settings.json"), """{ "SchemaVersion": 2, "MicrophoneId": "default", "Shortcuts": [{ "LocaleCode": "pl-PL", "VirtualKey": 82 }], "ModelIdleTimeoutMinutes": 7 }""");

        var imported = SettingsImport.Read(EarlierCopyFinder.FromChosenFolder(app)!, startWithWindows: false);

        Assert.Equal(10, imported.Settings.ModelIdleTimeoutMinutes);
        Assert.Contains("model idle timeout", imported.Warning);
    }

    [Fact]
    public void Offers_this_copys_own_settings_from_before_they_were_shared()
    {
        var current = Release("PrivateType 1.4.0", lastUsed: new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc));
        Release("PrivateType 1.3.0", lastUsed: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(current, EarlierCopyFinder.FindMostRecent(current, [])?.AppDirectory);
    }

    [Fact]
    public void Finds_the_copy_that_starts_with_windows_anywhere()
    {
        var current = Release(Path.Combine("downloads", "PrivateType 1.4.0"), withSettings: false);
        var startup = Release(Path.Combine("tools", "PrivateType 1.3.0"));

        Assert.Equal(startup, EarlierCopyFinder.FindMostRecent(current, [startup])?.AppDirectory);
    }

    [Fact]
    public void Settings_are_shared_beside_the_model_cache()
    {
        var app = Release("PrivateType 1.4.0", withSettings: false);
        var localAppData = Path.Combine(root, "LocalAppData");

        Assert.Equal(Path.Combine(localAppData, "PrivateType"), PortablePaths.SettingsDirectoryFor(app, localAppData));
    }

    [Fact]
    public void A_portable_copy_keeps_its_settings_in_its_own_folder()
    {
        var app = Release("PrivateType 1.4.0", withSettings: false);
        Directory.CreateDirectory(Path.Combine(app, "models"));

        Assert.Equal(Path.Combine(app, "data"), PortablePaths.SettingsDirectoryFor(app, Path.Combine(root, "LocalAppData")));
    }

    [Fact]
    public void Without_local_app_data_settings_stay_in_the_copy()
    {
        var app = Release("PrivateType 1.4.0", withSettings: false);

        Assert.Equal(Path.Combine(app, "data"), PortablePaths.SettingsDirectoryFor(app, null));
    }

    [Fact]
    public void Finds_nothing_when_no_other_copy_has_settings()
    {
        var current = Release("PrivateType 1.4.0", withSettings: false);
        Release("PrivateType 1.3.0", withSettings: false);
        Directory.CreateDirectory(Path.Combine(root, "Photos"));

        Assert.Null(EarlierCopyFinder.FindMostRecent(current, [Path.Combine(root, "missing", "app")]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("app")]
    [InlineData("app\\data")]
    public void A_chosen_folder_may_be_the_release_its_app_folder_or_its_data_folder(string inside)
    {
        var app = Release("PrivateType 1.3.0");

        Assert.Equal(app, EarlierCopyFinder.FromChosenFolder(Path.Combine(root, "PrivateType 1.3.0", inside))?.AppDirectory);
    }

    [Fact]
    public void A_chosen_folder_without_settings_is_not_a_copy()
    {
        Directory.CreateDirectory(Path.Combine(root, "Photos"));

        Assert.Null(EarlierCopyFinder.FromChosenFolder(Path.Combine(root, "Photos")));
    }

    [Fact]
    public void Imports_everything_except_windows_startup()
    {
        var app = Release("PrivateType 1.3.0", settings: PortableSettings.Default with
        {
            StartWithWindows = true,
            SpeechModel = SpeechModelCatalog.NemotronId,
            Vocabulary = [new VocabularyEntry("WireGuard", VocabularyScopes.Shared)],
            HistoryShortcut = new KeyChord(false, true, true, false, 0x48),
            ReadySoundVolume = 35
        });
        var target = Path.Combine(root, "PrivateType 1.4.0", "app", "data");

        SettingsImport.ImportInto(EarlierCopyFinder.FromChosenFolder(app)!, new PortableSettingsStore(target), target);

        var saved = new PortableSettingsStore(target).Load().Settings;
        Assert.False(saved.StartWithWindows);
        Assert.Equal(SpeechModelCatalog.NemotronId, saved.SpeechModel);
        Assert.Equal("WireGuard", Assert.Single(saved.Vocabulary).Phrase);
        Assert.Equal("Alt+Shift+H", saved.HistoryShortcut.Label);
        Assert.Equal(35, saved.ReadySoundVolume);
    }

    [Fact]
    public void A_damaged_custom_sound_falls_back_to_ping_and_keeps_the_other_settings()
    {
        var broken = Path.Combine(root, "broken.wav");
        Directory.CreateDirectory(root);
        File.WriteAllText(broken, "not audio");
        var app = Release("PrivateType 1.3.0", settings: PortableSettings.Default with
        {
            ReadySound = "custom",
            CustomReadySoundPath = broken,
            Vocabulary = [new VocabularyEntry("WireGuard", VocabularyScopes.Shared)]
        });
        var target = Path.Combine(root, "shared");

        var imported = SettingsImport.ImportInto(EarlierCopyFinder.FromChosenFolder(app)!, new PortableSettingsStore(target), target);

        var saved = new PortableSettingsStore(target).Load().Settings;
        Assert.Equal("ping", saved.ReadySound);
        Assert.Null(saved.CustomReadySoundPath);
        Assert.Equal("WireGuard", Assert.Single(saved.Vocabulary).Phrase);
        Assert.NotNull(imported.Warning);
    }

    [Fact]
    public void A_custom_sound_that_is_gone_falls_back_to_ping()
    {
        var app = Release("PrivateType 1.3.0", settings: PortableSettings.Default with
        {
            ReadySound = "custom",
            CustomReadySoundPath = Path.Combine(root, "gone.wav")
        });

        var imported = SettingsImport.Read(EarlierCopyFinder.FromChosenFolder(app)!, startWithWindows: true).Settings;

        Assert.Equal("ping", imported.ReadySound);
        Assert.Null(imported.CustomReadySoundPath);
        Assert.True(imported.StartWithWindows);
    }

    private string Release(string name, bool withSettings = true, DateTime? lastUsed = null, PortableSettings? settings = null)
    {
        var app = Path.Combine(root, name, "app");
        Directory.CreateDirectory(app);
        if (withSettings)
        {
            var data = Path.Combine(app, "data");
            new PortableSettingsStore(data).Save(settings ?? PortableSettings.Default);
            if (lastUsed is { } time)
                File.SetLastWriteTimeUtc(Path.Combine(data, "settings.json"), time);
        }

        return app;
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
