using System.Net.Http;
using System.IO;
using PrivateType.Core;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace PrivateType.App;

public sealed record MicrophoneOption(string Id, string DisplayName);

internal static class MicrophoneCatalog
{
    internal const string DefaultId = "default";
    private const string EndpointPrefix = "wasapi:";
    private const string LegacyWaveInPrefix = "wavein:";

    internal static IReadOnlyList<MicrophoneOption> Enumerate()
    {
        var microphones = new List<MicrophoneOption> { new(DefaultId, "System default") };
        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            using (device)
                microphones.Add(new(EndpointPrefix + device.ID, device.FriendlyName));
        }
        return microphones;
    }

    // Opens the saved endpoint, or the current Windows default when it is unavailable.
    internal static MMDevice Open(string microphoneId)
    {
        using var enumerator = new MMDeviceEnumerator();
        if (microphoneId.StartsWith(EndpointPrefix, StringComparison.Ordinal))
        {
            try
            {
                var device = enumerator.GetDevice(microphoneId[EndpointPrefix.Length..]);
                if (device.State == DeviceState.Active)
                    return device;
                device.Dispose();
            }
            catch (COMException)
            {
            }
        }

        try
        {
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
        }
        catch (COMException exception)
        {
            throw new InvalidOperationException("No microphone is available. Connect or enable a recording device.", exception);
        }
    }

    // Settings from v1.0.7 and earlier store an MME position ("wavein:N"), which shifts
    // whenever devices are added or removed. Map it to a stable endpoint once, by name.
    internal static string MigrateLegacyId(string microphoneId, IReadOnlyList<MicrophoneOption> microphones)
    {
        if (!microphoneId.StartsWith(LegacyWaveInPrefix, StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(microphoneId[LegacyWaveInPrefix.Length..], out var deviceNumber)
            || deviceNumber < 0
            || deviceNumber >= WaveIn.DeviceCount)
        {
            return microphoneId;
        }

        return MatchLegacyProductName(WaveIn.GetCapabilities(deviceNumber).ProductName, microphones) ?? microphoneId;
    }

    // MME truncates product names to 31 characters, so match by prefix and accept only one candidate.
    internal static string? MatchLegacyProductName(string productName, IReadOnlyList<MicrophoneOption> microphones)
    {
        if (string.IsNullOrWhiteSpace(productName))
            return null;

        var matches = microphones
            .Where(microphone => microphone.Id.StartsWith(EndpointPrefix, StringComparison.Ordinal)
                && microphone.DisplayName.StartsWith(productName, StringComparison.Ordinal))
            .ToList();
        return matches.Count == 1 ? matches[0].Id : null;
    }
}

internal static class PortablePaths
{
    // This copy's own data folder. Copies up to 1.3 kept their settings here.
    internal static string DataDirectory => DataDirectoryFor(AppContext.BaseDirectory);

    // Where settings and custom sounds live: one place shared by every copy for this Windows user, so an
    // update starts with your settings. A portable copy (one with app\models) keeps them in its own folder.
    internal static string SettingsDirectory => SettingsDirectoryFor(
        AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    internal static string SettingsDirectoryFor(string baseDirectory, string? localAppData)
    {
        var applicationDirectory = Path.GetFullPath(baseDirectory);
        if (Directory.Exists(Path.Combine(applicationDirectory, "models")) || string.IsNullOrWhiteSpace(localAppData) || !Path.IsPathRooted(localAppData))
            return DataDirectoryFor(applicationDirectory);
        return Path.Combine(Path.GetFullPath(localAppData), "PrivateType");
    }

    internal static void EnsureWritable()
        => EnsureWritable(AppContext.BaseDirectory);

    internal static void EnsureWritable(string baseDirectory)
    {
        try
        {
            var dataDirectory = DataDirectoryFor(baseDirectory);
            Directory.CreateDirectory(dataDirectory);
            var probe = Path.Combine(dataDirectory, $".write-probe-{Guid.NewGuid():N}");
            try
            {
                File.WriteAllText(probe, string.Empty);
            }
            finally
            {
                if (File.Exists(probe))
                    File.Delete(probe);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException("PrivateType needs a writable portable folder.", exception);
        }
    }

    private static string DataDirectoryFor(string baseDirectory)
        => Path.Combine(Path.GetFullPath(baseDirectory), "data");
}

// Resolves where each catalog model lives and hands out one provisioner per model.
internal sealed class SpeechModelLibrary(IModelDownloadClient downloader)
{
    private readonly Dictionary<string, (ModelStorageLocation Storage, ModelProvisioner Provisioner)> models = new(StringComparer.Ordinal);

    public ModelStorageLocation Storage(SpeechModelDefinition model) => Resolve(model).Storage;

    public ModelProvisioner Provisioner(SpeechModelDefinition model) => Resolve(model).Provisioner;

    private (ModelStorageLocation Storage, ModelProvisioner Provisioner) Resolve(SpeechModelDefinition model)
    {
        if (!models.TryGetValue(model.Id, out var entry))
        {
            var storage = ModelStoragePolicy.Resolve(model.Manifest);
            entry = (storage, new ModelProvisioner(storage.Directory, model.Manifest, downloader));
            models[model.Id] = entry;
        }
        return entry;
    }
}

internal sealed class HttpModelDownloadClient : IModelDownloadClient, IDisposable
{
    private readonly HttpClient client = new(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(15) })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    public async Task DownloadAsync(Uri source, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[128 * 1024];
        long downloaded = 0;
        while (true)
        {
            var read = await content.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            downloaded += read;
            progress?.Report(downloaded);
        }
    }

    public void Dispose() => client.Dispose();
}
