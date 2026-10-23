namespace PrivateType.Core;

public sealed record AudioFrameLevel(double Rms, int Bytes);

public sealed record VoiceSummary(double VoicedSeconds, double LastVoicedSeconds);

// Finds where the microphone last heard a voice, from the loudness of each captured frame, so a
// transcript that stops early can be told apart from a speaker who stopped talking. It leans
// towards hearing a voice: a false one costs one short extra transcription, a missed one loses words.
public static class VoiceActivity
{
    // A voice is this many times louder than the quietest tenth of the recording (12 dB)...
    private const double NoiseFloorRatio = 4;
    // ...but never needs more than about -46 dBFS, so even quiet speech without pauses counts...
    private const double MaximumVoiceThreshold = 0.005;
    // ...and never less than about -50 dBFS, so a silent room does not count as speech.
    private const double MinimumVoiceThreshold = 0.003;
    // Pauses between syllables shorter than this stay inside one stretch of speech, and a stretch
    // shorter than this is a click, such as the shortcut's key coming up.
    private const double ShortestPauseSeconds = 0.3;

    public static double Rms(ReadOnlySpan<byte> pcm16)
    {
        var samples = pcm16.Length / 2;
        if (samples == 0)
            return 0;

        var sumOfSquares = 0d;
        for (var index = 0; index < samples; index++)
        {
            var sample = (short)(pcm16[index * 2] | (pcm16[(index * 2) + 1] << 8)) / 32768d;
            sumOfSquares += sample * sample;
        }
        return Math.Sqrt(sumOfSquares / samples);
    }

    public static VoiceSummary Summarize(IReadOnlyList<AudioFrameLevel> frames, int bytesPerSecond)
    {
        if (frames.Count == 0)
            return new VoiceSummary(0, 0);

        var threshold = Math.Clamp(NoiseFloor(frames) * NoiseFloorRatio, MinimumVoiceThreshold, MaximumVoiceThreshold);
        double voiced = 0, lastVoiced = 0, runStart = 0, runEnd = 0, position = 0;
        var inRun = false;
        foreach (var frame in frames)
        {
            var frameEnd = position + (frame.Bytes / (double)bytesPerSecond);
            if (frame.Rms >= threshold)
            {
                if (!inRun)
                    runStart = position;
                inRun = true;
                runEnd = frameEnd;
            }
            else if (inRun && frameEnd - runEnd >= ShortestPauseSeconds)
            {
                EndRun();
            }
            position = frameEnd;
        }
        if (inRun)
            EndRun();

        return new VoiceSummary(voiced, lastVoiced);

        void EndRun()
        {
            inRun = false;
            if (runEnd - runStart < ShortestPauseSeconds)
                return;
            voiced += runEnd - runStart;
            lastVoiced = runEnd;
        }
    }

    private static double NoiseFloor(IReadOnlyList<AudioFrameLevel> frames)
    {
        var levels = frames.Select(frame => frame.Rms).Order().ToArray();
        return levels[levels.Length / 10];
    }
}
