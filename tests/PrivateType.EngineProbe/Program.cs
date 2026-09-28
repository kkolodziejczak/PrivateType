using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using PrivateType.EngineProbe;

// Developer-only Stage 1 probe for vocabulary boosting. It reads a manifest and WAV clips from a
// caller-supplied temporary directory and prints aggregate counts only: never transcripts, phrases,
// or audio paths. Not part of any release output.
//
// Usage: PrivateType.EngineProbe --engine <nemo-speech.exe> --model <model.gguf> --manifest <manifest.json> [--boosts 1,2,3,4,5]

var options = ParseArguments(args);
var manifest = JsonSerializer.Deserialize<ProbeManifest>(File.ReadAllText(options.Manifest), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
    ?? throw new InvalidOperationException("The manifest is empty.");
var clipDirectory = Path.GetDirectoryName(Path.GetFullPath(options.Manifest))!;

using var engine = await ProbeEngine.StartAsync(options.Engine, options.Model);
Console.WriteLine($"Engine ready. Clips: {manifest.Clips.Count(clip => clip.Kind == "target")} target, {manifest.Clips.Count(clip => clip.Kind == "control")} control; phrases: {manifest.Phrases.Count}.");

var invalidRejected = await engine.IsRejectedAsync(ProbeProtocol.InvalidContextSessionUpdate("en-US"));
Console.WriteLine($"Invalid speech_contexts shape rejected: {(invalidRejected ? "yes" : "no")}");

var conditions = new List<(string Name, IReadOnlyList<SpeechContext> Contexts)> { ("baseline", []) };
conditions.AddRange(options.Boosts.Select(boost => ($"boost {boost:0.##}", (IReadOnlyList<SpeechContext>)[new SpeechContext(manifest.Phrases, boost)])));

Console.WriteLine();
Console.WriteLine("condition   | locale | target terms recognized | controls with a boosted phrase inserted | failed requests");
foreach (var (name, contexts) in conditions)
{
    foreach (var locale in manifest.Clips.Select(clip => clip.Locale).Distinct())
    {
        int hits = 0, terms = 0, insertions = 0, controls = 0, failures = 0;
        foreach (var clip in manifest.Clips.Where(clip => clip.Locale == locale))
        {
            var transcript = await engine.TranscribeAsync(Path.Combine(clipDirectory, clip.File), ProbeProtocol.SessionUpdate(locale, contexts));
            if (transcript is null)
            {
                failures++;
                continue;
            }

            if (clip.Kind == "target")
            {
                terms += clip.Terms.Count;
                hits += clip.Terms.Count(term => ProbeProtocol.CountOccurrences(transcript, term) > 0);
            }
            else
            {
                controls++;
                if (manifest.Phrases.Any(phrase => ProbeProtocol.CountOccurrences(transcript, phrase) > clip.Allowed(phrase)))
                    insertions++;
            }
        }

        Console.WriteLine($"{name,-11} | {locale,-6} | {hits,3}/{terms,-3}                 | {insertions,3}/{controls,-3}                                 | {failures}");
    }
}

var boundary = ProbeProtocol.BoundaryPhrases(200, 16 * 1024);
var boundaryClip = manifest.Clips.First(clip => clip.Kind == "control");
var stopwatch = Stopwatch.StartNew();
var boundaryTranscript = await engine.TranscribeAsync(
    Path.Combine(clipDirectory, boundaryClip.File),
    ProbeProtocol.SessionUpdate(boundaryClip.Locale, [new SpeechContext(boundary, options.Boosts.Max())]));
Console.WriteLine();
Console.WriteLine($"Boundary: {boundary.Count} phrases, {ProbeProtocol.NormalizedBytes(boundary)} normalized bytes -> " +
    (boundaryTranscript is null ? "FAILED" : $"completed in {stopwatch.ElapsedMilliseconds} ms; boundary phrases inserted: {boundary.Count(phrase => ProbeProtocol.CountOccurrences(boundaryTranscript, phrase) > 0)}"));
return 0;

static ProbeOptions ParseArguments(string[] args)
{
    string Value(string name) =>
        args.SkipWhile(arg => arg != name).Skip(1).FirstOrDefault() ?? throw new ArgumentException($"Missing {name}.");

    var boosts = args.Contains("--boosts")
        ? Value("--boosts").Split(',').Select(value => double.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray()
        : [1.0, 2.0, 3.0, 4.0, 5.0];
    return new ProbeOptions(Value("--engine"), Value("--model"), Value("--manifest"), boosts);
}

internal sealed record ProbeOptions(string Engine, string Model, string Manifest, IReadOnlyList<double> Boosts);

internal sealed record ProbeManifest(IReadOnlyList<string> Phrases, IReadOnlyList<ProbeClip> Clips);

internal sealed record ProbeClip(string File, string Locale, string Kind, IReadOnlyList<string> Terms)
{
    // Control clips may legitimately contain a boosted phrase; only extra occurrences count as insertions.
    public int Allowed(string phrase) => Terms.Count(term => string.Equals(term, phrase, StringComparison.OrdinalIgnoreCase));
}

internal sealed class ProbeEngine : IDisposable
{
    private readonly Process process;
    private readonly int port;

    private ProbeEngine(Process process, int port)
    {
        this.process = process;
        this.port = port;
    }

    public static async Task<ProbeEngine> StartAsync(string executable, string model)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var process = Process.Start(new ProcessStartInfo(executable,
            $"serve --host 127.0.0.1 --port {port} --threads 1 --no-ui --asr-model \"{model}\" --device cpu")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        }) ?? throw new InvalidOperationException("Could not start the engine.");
        // Drain engine logs; surface only boosting warnings, which carry no audio or transcript content.
        process.ErrorDataReceived += (_, line) =>
        {
            if (line.Data?.Contains("boost", StringComparison.OrdinalIgnoreCase) == true)
                Console.WriteLine($"engine: {line.Data}");
        };
        process.OutputDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
                throw new InvalidOperationException("The engine exited before becoming ready.");
            try
            {
                if ((await client.GetAsync($"http://127.0.0.1:{port}/ready")).IsSuccessStatusCode)
                    return new ProbeEngine(process, port);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
            }
            await Task.Delay(250);
        }

        process.Kill(entireProcessTree: true);
        throw new TimeoutException("The engine did not become ready.");
    }

    public async Task<bool> IsRejectedAsync(string sessionUpdate)
    {
        using var socket = await ConnectAsync();
        await SendTextAsync(socket, sessionUpdate);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            while (true)
            {
                var payload = await ReceiveAsync(socket, timeout.Token);
                if (payload is null)
                    return true;
                if (ProbeProtocol.ParseEvent(payload).Kind == ProbeEventKind.Error)
                    return true;
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    // Returns the completed transcript, or null when the engine reported an error or timed out.
    public async Task<string?> TranscribeAsync(string wavPath, string sessionUpdate)
    {
        var pcm = ReadPcm16Mono16k(wavPath);
        using var socket = await ConnectAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await SendTextAsync(socket, sessionUpdate);
        var receive = ReceiveTranscriptAsync(socket, timeout.Token);
        for (var offset = 0; offset < pcm.Length; offset += 3200)
            await socket.SendAsync(pcm.AsMemory(offset, Math.Min(3200, pcm.Length - offset)), WebSocketMessageType.Binary, true, timeout.Token);
        await SendTextAsync(socket, "{\"type\":\"input_audio_buffer.commit\"}");
        try
        {
            return await receive;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (!process.HasExited)
            process.Kill(entireProcessTree: true);
        process.Dispose();
    }

    private async Task<ClientWebSocket> ConnectAsync()
    {
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/v1/realtime"), CancellationToken.None);
        return socket;
    }

    private static async Task<string?> ReceiveTranscriptAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        while (true)
        {
            var payload = await ReceiveAsync(socket, cancellationToken);
            if (payload is null)
                return null;
            var probeEvent = ProbeProtocol.ParseEvent(payload);
            if (probeEvent.Kind == ProbeEventKind.Completed)
                return probeEvent.Text;
            if (probeEvent.Kind == ProbeEventKind.Error)
                return null;
        }
    }

    private static Task SendTextAsync(ClientWebSocket socket, string text) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);

    private static async Task<string?> ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[32_768];
        using var message = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                return Encoding.UTF8.GetString(message.ToArray());
        }
    }

    private static byte[] ReadPcm16Mono16k(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var offset = 12;
        int? sampleRate = null, channels = null, bits = null;
        while (offset + 8 <= bytes.Length)
        {
            var id = Encoding.ASCII.GetString(bytes, offset, 4);
            var size = BitConverter.ToInt32(bytes, offset + 4);
            if (id == "fmt ")
            {
                channels = BitConverter.ToInt16(bytes, offset + 10);
                sampleRate = BitConverter.ToInt32(bytes, offset + 12);
                bits = BitConverter.ToInt16(bytes, offset + 22);
            }
            else if (id == "data")
            {
                if (sampleRate != 16000 || channels != 1 || bits != 16)
                    throw new InvalidDataException("Probe clips must be 16 kHz mono PCM16 WAV.");
                return bytes.AsSpan(offset + 8, Math.Min(size, bytes.Length - offset - 8)).ToArray();
            }
            offset += 8 + size + (size & 1);
        }

        throw new InvalidDataException("The WAV file has no data chunk.");
    }
}
