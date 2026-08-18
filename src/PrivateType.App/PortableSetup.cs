using System.Net.Http;
using System.IO;
using PrivateType.Core;
using NAudio.Wave;

namespace PrivateType.App;

public sealed record MicrophoneOption(string Id, string DisplayName);

internal static class MicrophoneCatalog
{
    internal static IReadOnlyList<MicrophoneOption> Enumerate()
    {
        var microphones = new List<MicrophoneOption> { new("default", "System default") };
        for (var index = 0; index < WaveIn.DeviceCount; index++)
            microphones.Add(new($"wavein:{index}", WaveIn.GetCapabilities(index).ProductName));
        return microphones;
    }

    internal static int ToDeviceNumber(string microphoneId)
    {
        if (string.Equals(microphoneId, "default", StringComparison.OrdinalIgnoreCase))
            return -1;

        return microphoneId.StartsWith("wavein:", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(microphoneId["wavein:".Length..], out var deviceNumber)
            && deviceNumber >= 0
            && deviceNumber < WaveIn.DeviceCount
                ? deviceNumber
                : -1;
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
