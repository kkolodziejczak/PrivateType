using System.IO;
using NAudio.Wave;
using PrivateType.Core;

namespace PrivateType.App;

internal static class ReadySoundAudio
{
    internal static ReadySoundClip Load(PortableSettings settings, bool fallbackToPing)
    {
        ReadySoundClip clip;
        try
        {
            clip = settings.ReadySound switch
            {
                "custom" => ReadCustom(settings.CustomReadySoundPath),
                "chime" => Synthesize(false),
                "bell" => Synthesize(true),
                _ => ReadPing()
            };
        }
        catch (Exception exception) when (fallbackToPing && settings.ReadySound == "custom" && IsFileError(exception))
        {
            clip = ReadPing();
        }
        var peak = clip.Samples.Max(sample => Math.Abs(sample));
        var gain = peak > 0 ? 0.95f / peak * Math.Clamp(settings.ReadySoundVolume, 0, 100) / 100f : 0;
        for (var index = 0; index < clip.Samples.Length; index++)
            clip.Samples[index] *= gain;
        return clip;
    }

    internal static ReadySoundClip ReadCustom(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Choose a WAV or MP3 file first.");
        var extension = Path.GetExtension(path);
        if (!extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a WAV or MP3 sound file.");
        using var reader = new AudioFileReader(path);
        return ReadClip(reader);
    }

    private static ReadySoundClip ReadClip(ISampleProvider source)
    {
        var format = source.WaveFormat;
        if (format.SampleRate is < 8000 or > 192000 || format.Channels is < 1 or > 8)
            throw new InvalidDataException("The sound's audio format is not supported.");
        var samples = new float[format.SampleRate * format.Channels * 3];
        var length = 0;
        while (length < samples.Length)
        {
            var count = source.Read(samples, length, samples.Length - length);
            if (count == 0)
                break;
            length += count;
        }
        if (length == 0 || samples.Take(length).Any(sample => !float.IsFinite(sample)))
            throw new InvalidDataException("The sound file contains no playable audio.");
        Array.Resize(ref samples, length);
        var fadeFrames = Math.Min(format.SampleRate / 100, length / format.Channels);
        for (var frame = 0; frame < fadeFrames; frame++)
            for (var channel = 0; channel < format.Channels; channel++)
                samples[length - (frame + 1) * format.Channels + channel] *= (float)frame / fadeFrames;
        return new ReadySoundClip(format, samples);
    }

    private static ReadySoundClip ReadPing()
    {
        using var stream = typeof(ModelReadySound).Assembly.GetManifestResourceStream("PrivateType.ModelReady.wav")
            ?? throw new InvalidOperationException("The model-ready sound is missing.");
        using var reader = new WaveFileReader(stream);
        return ReadClip(reader.ToSampleProvider());
    }

    private static ReadySoundClip Synthesize(bool bell)
    {
        const int sampleRate = 44100;
        var samples = new float[(int)(sampleRate * (bell ? 1.1 : 0.85))];
        for (var index = 0; index < samples.Length; index++)
        {
            var time = (double)index / sampleRate;
            var attack = Math.Min(time / 0.008, 1);
            var tone = bell
                ? Math.Sin(2 * Math.PI * 880 * time) + 0.35 * Math.Sin(2 * Math.PI * 1763 * time)
                : Math.Sin(2 * Math.PI * 660 * time) * Math.Exp(-12 * time) +
                  (time < 0.2 ? 0 : Math.Sin(2 * Math.PI * 880 * (time - 0.2)) * Math.Min((time - 0.2) / 0.008, 1));
            samples[index] = (float)(tone * attack * Math.Exp(-5 * time));
        }
        return ReadClip(new ReadySoundClip(WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1), samples));
    }

    internal static bool IsFileError(Exception exception) => exception is IOException or UnauthorizedAccessException
        or ArgumentException or InvalidOperationException or NotSupportedException or FormatException
        or System.Runtime.InteropServices.COMException;
}

internal sealed class ReadySoundClip(WaveFormat waveFormat, float[] samples) : ISampleProvider
{
    private int position;
    public WaveFormat WaveFormat { get; } = waveFormat;
    internal float[] Samples { get; } = samples;

    public int Read(float[] buffer, int offset, int count)
    {
        var available = Math.Min(count, Samples.Length - position);
        Array.Copy(Samples, position, buffer, offset, available);
        position += available;
        return available;
    }
}
