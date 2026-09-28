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
    internal static string DataDirectory => DataDirectoryFor(AppContext.BaseDirectory);

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

internal static class PinnedModel
{
    internal static readonly ModelManifest Manifest = new(
        "nemotron-3.5-asr-streaming-0.6b-q8_0-1c8deae",
        new Uri("https://huggingface.co/nvidia/nemotron-3.5-asr-streaming-0.6b/resolve/1c8deaecc64b91f034d73e08dd8b64625eb3395d/nemotron-3.5-asr-streaming-0.6b.q8_0.gguf"),
        "nemotron-3.5-asr-streaming-0.6b.q8_0.gguf",
        741548352L,
        "a5c435f294eea8f88ce68dd27b8c3bfea7f777cb2fbba04fcd30eaa555f429ae");
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
