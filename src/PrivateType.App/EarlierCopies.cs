using System.IO;
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

// Copies up to 1.3 kept settings in their own folder. They are found through this copy's own folder,
// the Windows startup entry, and the releases unpacked beside this one; the last one changed wins.
internal static class EarlierCopyFinder
{
    private const int MaximumSiblings = 200;

    public static EarlierCopy? FindMostRecent(string currentAppDirectory, IEnumerable<string> otherAppDirectories)
    {
        var current = Normalize(currentAppDirectory);
        return otherAppDirectories
            .Prepend(current)
            .Concat(SiblingAppDirectories(current))
            .Select(Normalize)
            .Where(directory => directory.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(Inspect)
            .OfType<EarlierCopy>()
            .OrderByDescending(copy => copy.LastUsedUtc)
            .FirstOrDefault();
    }

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

    // For the first start: copies the custom sound, then saves. A sound that can't be copied falls back
    // to Ping with a warning rather than losing the rest of the settings.
    public static ImportedSettings ImportInto(EarlierCopy copy, PortableSettingsStore store, string dataDirectory)
    {
        var (imported, warning) = Read(copy, startWithWindows: false);
        if (imported.ReadySound == "custom")
        {
            try
            {
                imported = imported with { CustomReadySoundPath = ReadySoundStorage.Import(imported.CustomReadySoundPath!, dataDirectory) };
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                imported = imported with { ReadySound = PortableSettings.Default.ReadySound, CustomReadySoundPath = null };
                warning = $"{warning} The custom ready sound could not be copied, so Ping is used.".Trim();
            }
        }
        store.Save(imported);
        return new ImportedSettings(imported, warning);
    }
}
