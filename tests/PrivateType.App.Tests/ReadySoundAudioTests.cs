using System.IO;
using NAudio.Wave;
using PrivateType.App;
using PrivateType.Core;
using Xunit;

namespace PrivateType.App.Tests;

public sealed class ReadySoundAudioTests
{
    [Fact]
    public void Playback_adapter_reads_samples_into_its_byte_backed_buffer()
    {
        var clip = new ReadySoundClip(WaveFormat.CreateIeeeFloatWaveFormat(44100, 1), [0.25f, -0.5f, 1f]);
        var playback = clip.ToWaveProvider();
        var buffer = Enumerable.Repeat((byte)0x7F, 16).ToArray();

        Assert.Equal(8, playback.Read(buffer, 4, 8));
        Assert.Equal(0.25f, BitConverter.ToSingle(buffer, 4));
        Assert.Equal(-0.5f, BitConverter.ToSingle(buffer, 8));
        Assert.All(buffer.Take(4).Concat(buffer.Skip(12)), value => Assert.Equal((byte)0x7F, value));
        Assert.Equal(4, playback.Read(buffer, 0, 8));
        Assert.Equal(1f, BitConverter.ToSingle(buffer, 0));
        Assert.Equal(0, playback.Read(buffer, 0, 8));
    }

    [Theory]
    [InlineData("ping")]
    [InlineData("chime")]
    [InlineData("bell")]
    public void Built_in_sounds_are_audible_bounded_and_use_selected_volume(string sound)
    {
        var clip = ReadySoundAudio.Load(PortableSettings.Default with { ReadySound = sound, ReadySoundVolume = 80 }, false);
        Assert.InRange(clip.Samples.Length / (double)(clip.WaveFormat.SampleRate * clip.WaveFormat.Channels), 0.1, 3);
        Assert.InRange(clip.Samples.Max(sample => Math.Abs(sample)), 0.7599f, 0.7601f);
        var halfVolume = ReadySoundAudio.Load(PortableSettings.Default with { ReadySound = sound, ReadySoundVolume = 40 }, false);
        Assert.Equal(clip.Samples.Select(sample => sample / 2).ToArray(), halfVolume.Samples);
    }

    [Fact]
    public void Zero_volume_produces_silence()
    {
        var clip = ReadySoundAudio.Load(PortableSettings.Default with { ReadySoundVolume = 0 }, false);
        Assert.All(clip.Samples, sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void Missing_custom_file_falls_back_only_for_normal_playback()
    {
        var settings = PortableSettings.Default with { ReadySound = "custom", CustomReadySoundPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.wav") };
        var fallback = ReadySoundAudio.Load(settings, true);
        var ping = ReadySoundAudio.Load(PortableSettings.Default, false);
        Assert.Equal(ping.Samples, fallback.Samples);
        Assert.Throws<FileNotFoundException>(() => ReadySoundAudio.Load(settings, false));
    }

    [Fact]
    public void Long_custom_audio_is_trimmed_to_three_seconds_with_faded_end()
    {
        WithDirectory(directory =>
        {
            var path = WriteWave(directory, 5);
            var clip = ReadySoundAudio.Load(PortableSettings.Default with { ReadySound = "custom", CustomReadySoundPath = path }, false);
            Assert.Equal(3 * 16000, clip.Samples.Length);
            Assert.Equal(0, clip.Samples[^1]);
        });
    }

    [Fact]
    public void Import_copies_audio_deduplicates_and_survives_original_removal()
    {
        WithDirectory(directory =>
        {
            var source = WriteWave(directory, 1);
            var dataDirectory = Path.Combine(directory, "app-data");
            var imported = ReadySoundStorage.Import(source, dataDirectory);
            Assert.Equal(Path.GetFileName(source), Path.GetFileName(imported));
            Assert.StartsWith(Path.GetFullPath(dataDirectory) + Path.DirectorySeparatorChar, imported);
            Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(imported));
            Assert.Equal(imported, ReadySoundStorage.Import(source, dataDirectory));
            Assert.Single(Directory.GetFiles(Path.GetDirectoryName(imported)!));
            File.Delete(source);
            ReadySoundStorage.Validate(imported);
        });
    }

    [Fact]
    public void Invalid_custom_file_is_rejected_without_an_imported_copy()
    {
        WithDirectory(directory =>
        {
            var path = Path.Combine(directory, "broken.wav");
            File.WriteAllText(path, "not audio");
            var dataDirectory = Path.Combine(directory, "app-data");
            Assert.ThrowsAny<Exception>(() => ReadySoundStorage.Import(path, dataDirectory));
            Assert.False(Directory.Exists(dataDirectory));
            Assert.Equal(ReadySoundAudio.Load(PortableSettings.Default, false).Samples,
                ReadySoundAudio.Load(PortableSettings.Default with { ReadySound = "custom", CustomReadySoundPath = path }, true).Samples);
        });
    }

    [Fact]
    public void Unsupported_file_extension_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => ReadySoundStorage.Validate("sound.flac"));
    }

    private static string WriteWave(string directory, int seconds)
    {
        var path = Path.Combine(directory, "source.wav");
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(16000, 1));
        var samples = Enumerable.Range(0, 16000 * seconds).Select(index => (float)(0.25 * Math.Sin(2 * Math.PI * 440 * index / 16000))).ToArray();
        writer.WriteSamples(samples, 0, samples.Length);
        return path;
    }

    private static void WithDirectory(Action<string> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"PrivateType-ready-sound-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try { test(directory); }
        finally { Directory.Delete(directory, true); }
    }
}
