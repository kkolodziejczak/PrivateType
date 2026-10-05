using System.IO;
using System.Text.Json;
using PrivateType.Core;

namespace PrivateType.App;

// Another PrivateType copy on this computer, identified by its app folder (the one holding data\settings.json).
internal sealed record EarlierCopy(string AppDirectory, Version? Version, DateTime LastUsedUtc)
{
    public string DataDirectory => Path.Combine(AppDirectory, "data");

    public string Label => ApplicationVersion.Label(Version);

    // The folder the user unpacked: the release folder above app, when there is one.
    public string Folder => IsAppFolder(AppDirectory) ? Path.GetDirectoryName(AppDirectory)! : AppDirectory;

    internal static bool IsAppFolder(string directory) =>
        string.Equals(Path.GetFileName(directory), "app", StringComparison.OrdinalIgnoreCase);
}

// Each release folder starts without settings. Copies record themselves from now on; copies up to
// 1.3 do not, so the Windows startup entry and the folders beside this release are searched too.
internal static class EarlierCopyFinder
{
    private const int MaximumSiblings = 200;

    // Recorded copies are listed newest launch first, which is the best sign of the copy in use. Older
    // copies never recorded themselves, so for them the last settings change has to do.
    public static EarlierCopy? FindMostRecent(string currentAppDirectory, IEnumerable<string> recordedAppDirectories, IEnumerable<string> otherAppDirectories)
    {
        var current = Normalize(currentAppDirectory);
        return Candidates(recordedAppDirectories, current).FirstOrDefault()
            ?? Candidates(otherAppDirectories.Concat(SiblingAppDirectories(current)), current)
                .OrderByDescending(copy => copy.LastUsedUtc)
                .FirstOrDefault();
    }

    private static IEnumerable<EarlierCopy> Candidates(IEnumerable<string> appDirectories, string current) =>
        appDirectories
            .Select(Normalize)
            .Where(directory => directory.Length > 0 && !string.Equals(directory, current, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(Inspect)
            .OfType<EarlierCopy>();

    // Accepts the folder the user unpacked, its app folder, or the data folder inside it.
    public static EarlierCopy? FromChosenFolder(string folder)
    {
        var chosen = Normalize(folder);
        return new[] { Path.Combine(chosen, "app"), chosen, Path.GetDirectoryName(chosen) ?? chosen }
            .Select(Inspect)
            .OfType<EarlierCopy>()
            .FirstOrDefault();
    }

    // Release layout: <parent>\PrivateType 1.3.0\app. Other releases are usually unpacked beside it.
    private static IEnumerable<string> SiblingAppDirectories(string currentAppDirectory)
    {
        if (!EarlierCopy.IsAppFolder(currentAppDirectory) || Path.GetDirectoryName(Path.GetDirectoryName(currentAppDirectory)) is not { } parent)
            return [];
        try
        {
            return Directory.EnumerateDirectories(parent)
                .Select(release => Path.Combine(release, "app"))
                .Where(app => File.Exists(Path.Combine(app, "data", "settings.json")))
                .Take(MaximumSiblings)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static EarlierCopy? Inspect(string appDirectory)
    {
        try
        {
            var settings = new FileInfo(Path.Combine(appDirectory, "data", "settings.json"));
            if (!settings.Exists)
                return null;
            var version = WindowsStartupRegistration.VersionOf(Path.Combine(appDirectory, "PrivateType.exe"));
            return new EarlierCopy(Normalize(appDirectory), version, settings.LastWriteTimeUtc);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static string Normalize(string directory)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }
    }
}

// The app folders of copies that have run, kept beside the shared model cache. Only folder paths are
// stored; never settings, vocabulary, or dictated text.
internal sealed class KnownCopies(string listPath)
{
    private const int MaximumCopies = 20;

    public static KnownCopies ForCurrentUser() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrivateType", "copies.json"));

    public IReadOnlyList<string> Read()
    {
        try
        {
            return File.Exists(listPath)
                ? JsonSerializer.Deserialize<string[]>(File.ReadAllText(listPath)) ?? []
                : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    // Newest first; copies whose folder is gone drop off. Two copies starting together may lose one
    // entry, which only means a later prompt falls back to searching.
    public void Remember(string appDirectory)
    {
        var copies = Read()
            .Where(directory => !string.Equals(directory, appDirectory, StringComparison.OrdinalIgnoreCase) && Directory.Exists(directory))
            .Prepend(appDirectory)
            .Take(MaximumCopies)
            .ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(listPath)!);
        var temporaryPath = $"{listPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(copies));
            File.Move(temporaryPath, listPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}

// Warning names settings that were damaged in the earlier copy and reset to defaults.
internal sealed record ImportedSettings(PortableSettings Settings, string? Warning);

internal static class SettingsImport
{
    // Everything except Windows startup, which this copy decides through its own startup prompt.
    // A custom sound still points into the earlier copy; saving copies it into this copy's storage.
    // Throws IOException when the copy's settings can't be read, rather than offering defaults as its settings.
    public static ImportedSettings Read(EarlierCopy copy, bool startWithWindows)
    {
        SettingsLoadResult loaded;
        try
        {
            loaded = new PortableSettingsStore(copy.DataDirectory).Load();
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new IOException("The earlier copy's settings are not readable.", exception);
        }
        if (loaded.Unreadable)
            throw new IOException("The earlier copy's settings are damaged.");

        var imported = loaded.Settings with { StartWithWindows = startWithWindows };
        if (imported.ReadySound == "custom" && !File.Exists(imported.CustomReadySoundPath))
            imported = imported with { ReadySound = PortableSettings.Default.ReadySound, CustomReadySoundPath = null };
        return new ImportedSettings(imported, loaded.Warning);
    }

    // For the first start of a new copy: copies the custom sound, then saves.
    public static PortableSettings ImportInto(EarlierCopy copy, PortableSettingsStore store, string dataDirectory)
    {
        var imported = Read(copy, startWithWindows: false).Settings;
        if (imported.ReadySound == "custom")
            imported = imported with { CustomReadySoundPath = ReadySoundStorage.Import(imported.CustomReadySoundPath!, dataDirectory) };
        store.Save(imported);
        return imported;
    }
}
