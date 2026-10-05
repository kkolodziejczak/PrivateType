using PrivateType.Core;
using Xunit;

namespace PrivateType.Core.Tests;

public sealed class TranscriptionTimeForecastTests
{
    [Fact]
    public void Short_dictations_are_not_announced_before_anything_is_learned()
    {
        var forecast = new TranscriptionTimeForecast();

        Assert.False(forecast.IsWorthAnnouncing(TimeSpan.FromSeconds(5)));
        Assert.True(forecast.IsWorthAnnouncing(TimeSpan.FromSeconds(15)));
    }

    [Fact]
    public void Learns_a_slower_computer_and_announces_shorter_dictations()
    {
        var forecast = new TranscriptionTimeForecast();

        for (var dictation = 0; dictation < 5; dictation++)
            forecast.Learn(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2));

        Assert.True(forecast.IsWorthAnnouncing(TimeSpan.FromSeconds(6)));
        Assert.InRange(forecast.Predict(TimeSpan.FromSeconds(10)).TotalSeconds, 1.6, 2.0);
    }

    [Fact]
    public void Learns_a_faster_computer_and_stays_silent_longer()
    {
        var forecast = new TranscriptionTimeForecast();

        for (var dictation = 0; dictation < 5; dictation++)
            forecast.Learn(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(0.4));

        Assert.False(forecast.IsWorthAnnouncing(TimeSpan.FromSeconds(15)));
    }

    [Fact]
    public void Ignores_very_short_recordings_dominated_by_fixed_overhead()
    {
        var forecast = new TranscriptionTimeForecast();

        forecast.Learn(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3));

        Assert.Equal(TimeSpan.FromSeconds(10 * TranscriptionTimeForecast.InitialSecondsPerSpokenSecond), forecast.Predict(TimeSpan.FromSeconds(10)));
    }
}
