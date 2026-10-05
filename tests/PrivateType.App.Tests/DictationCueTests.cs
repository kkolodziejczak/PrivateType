using System.Speech.Synthesis;
using PrivateType.App;
using Xunit;

namespace PrivateType.App.Tests;

public sealed class DictationCueTests
{
    [Fact]
    public void Follows_the_window_being_dictated_into()
    {
        Assert.True(DictationBubble.FollowsForegroundWindow(0x1234, 42, 7, "Chrome_WidgetWin_1"));
    }

    [Theory]
    [InlineData(0, 42u, "Chrome_WidgetWin_1")]
    [InlineData(0x1234, 0u, "Chrome_WidgetWin_1")]
    [InlineData(0x1234, 7u, "HwndWrapper")]
    [InlineData(0x1234, 42u, "Progman")]
    [InlineData(0x1234, 42u, "WorkerW")]
    public void Falls_back_to_the_pointer_without_a_dictation_window(long foreground, uint processId, string className)
    {
        Assert.False(DictationBubble.FollowsForegroundWindow((nint)foreground, processId, 7, className));
    }

    [Fact]
    public void Spoken_cues_render_audible_audio_at_the_chosen_volume()
    {
        using (var synthesizer = new SpeechSynthesizer())
        {
            // Machines without a Windows voice skip spoken cues; the app records a diagnostic instead.
            if (synthesizer.GetInstalledVoices().Count == 0)
                return;
        }

        SpokenCue.Prepare();
        foreach (var phrase in new[] { SpokenCue.LoadingModel, SpokenCue.Transcribing })
        {
            var loud = SpokenCue.LoadIfPrepared(phrase, 100)!;
            var muted = SpokenCue.LoadIfPrepared(phrase, 0)!;

            Assert.InRange(loud.Samples.Max(Math.Abs), 0.9f, 0.96f);
            Assert.All(muted.Samples, sample => Assert.Equal(0, sample));
            Assert.InRange(loud.Samples.Length / (double)(loud.WaveFormat.SampleRate * loud.WaveFormat.Channels), 0.3, 3);
        }
    }
}
