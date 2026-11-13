using PrivateType.App;
using Xunit;

namespace PrivateType.App.Tests;

public sealed class CopySwitchTests
{
    private const string Current = "C:\\Apps\\PrivateType 1.6.1\\app\\PrivateType.exe";
    private const string Earlier = "C:\\Apps\\PrivateType 1.5.3\\app\\PrivateType.exe";

    [Fact]
    public void Starts_when_no_other_copy_runs_and_startup_needs_no_answer()
    {
        Assert.Equal(CopyStartDecision.Start, CopyStartPolicy.Decide(Current, [], StartupOwnershipDecision.KeepRegistered));
        Assert.Equal(CopyStartDecision.Start, CopyStartPolicy.Decide(Current, [], StartupOwnershipDecision.ClaimCurrent));
    }

    [Fact]
    public void Asks_when_another_copy_is_running()
    {
        Assert.Equal(CopyStartDecision.Ask, CopyStartPolicy.Decide(Current, [Earlier], StartupOwnershipDecision.LeaveDisabled));
    }

    [Fact]
    public void Asks_when_windows_starts_a_different_copy()
    {
        Assert.Equal(CopyStartDecision.Ask, CopyStartPolicy.Decide(Current, [], StartupOwnershipDecision.ConfirmCurrent));
    }

    // Opening the same copy twice is not a choice between versions.
    [Fact]
    public void Recognises_the_same_copy_already_running_whatever_the_path_case()
    {
        Assert.Equal(
            CopyStartDecision.AlreadyRunning,
            CopyStartPolicy.Decide(Current, [Earlier, Current.ToLowerInvariant()], StartupOwnershipDecision.ConfirmCurrent));
    }

    // Two copies started together must not both ask, or both give way: only the later one weighs the earlier one.
    [Fact]
    public void Weighs_only_copies_that_started_before_this_one()
    {
        var start = new DateTime(2026, 11, 13, 20, 0, 0, DateTimeKind.Local);
        RunningCopy CopyAt(DateTime startTime, int processId) => new(processId, Earlier, startTime, new Version(1, 5, 3));

        Assert.True(RunningCopies.StartedBefore(CopyAt(start.AddSeconds(-1), 900), start, 500));
        Assert.False(RunningCopies.StartedBefore(CopyAt(start.AddSeconds(1), 100), start, 500));
        // The same instant: the lower process id counts as first, so exactly one of the two gives way.
        Assert.True(RunningCopies.StartedBefore(CopyAt(start, 100), start, 500));
        Assert.False(RunningCopies.StartedBefore(CopyAt(start, 900), start, 500));
    }

    // Another program that happens to be called PrivateType.exe is never treated as a copy, so never closed.
    [Fact]
    public void Recognises_only_PrivateType_by_its_product_identity()
    {
        var directory = AppContext.BaseDirectory;

        Assert.True(RunningCopies.IsPrivateType(Path.Combine(directory, "PrivateType.dll")));
        Assert.False(RunningCopies.IsPrivateType(Path.Combine(directory, "PrivateType.App.Tests.dll")));
        Assert.False(RunningCopies.IsPrivateType(Path.Combine(directory, "missing.exe")));
    }

    [Fact]
    public void Tells_a_launcher_from_the_application_it_starts()
    {
        var release = Directory.CreateTempSubdirectory("privatetype-copy-");
        try
        {
            var application = Path.Combine(release.FullName, "app", "PrivateType.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(application)!);
            File.WriteAllText(application, "");
            var launcher = Path.Combine(release.FullName, "PrivateType.exe");

            Assert.True(RunningCopies.IsLauncher(launcher));
            Assert.False(RunningCopies.IsLauncher(application));
        }
        finally
        {
            release.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_listening_copy_quits_when_another_asks_it_to()
    {
        // A process id no running process uses here, so the test never signals a real copy.
        var processId = int.MaxValue - Environment.ProcessId;
        var quit = new TaskCompletionSource();
        Assert.False(CopyQuitSignal.TryRequest(processId));

        using (CopyQuitSignal.Listen(processId, () => quit.TrySetResult()))
        {
            Assert.True(CopyQuitSignal.IsListening(processId));
            Assert.True(CopyQuitSignal.TryRequest(processId));
            await quit.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.False(CopyQuitSignal.TryRequest(processId));
    }

    [Fact]
    public void Names_both_copies_by_version_on_the_choice()
    {
        Assert.Equal("Use PrivateType 1.6.1?", ChooseVersionWindow.TitleText(new Version(1, 6, 1)));
        Assert.Equal("Keep 1.5.3", ChooseVersionWindow.KeepText(new Version(1, 5, 3)));
        Assert.Equal("Use 1.6.1", ChooseVersionWindow.UseText(new Version(1, 6, 1)));
        Assert.Equal("Keep other copy", ChooseVersionWindow.KeepText(null));
        Assert.Equal("Use this copy", ChooseVersionWindow.UseText(null));
    }

    [Fact]
    public void Says_what_using_this_copy_does()
    {
        Assert.Equal(
            "Settings and speech models are shared, so nothing needs moving. Using 1.6.1 closes 1.5.3 and starts 1.6.1 with Windows.",
            ChooseVersionWindow.ExplanationText(new Version(1, 5, 3), runs: true, claimsStartup: true, new Version(1, 6, 1)));
        Assert.Equal(
            "Settings and speech models are shared, so nothing needs moving. Using 1.6.1 closes 1.5.3.",
            ChooseVersionWindow.ExplanationText(new Version(1, 5, 3), runs: true, claimsStartup: false, new Version(1, 6, 1)));
        Assert.Equal(
            "Settings and speech models are shared, so nothing needs moving. You can run either version; this chooses the one that starts with Windows.",
            ChooseVersionWindow.ExplanationText(new Version(1, 5, 3), runs: false, claimsStartup: true, new Version(1, 6, 1)));
        Assert.Equal(
            "1.5.3 is closed at once. A dictation in progress is lost.",
            ChooseVersionWindow.ForcedCloseText(new Version(1, 5, 3)));
        Assert.Equal(
            "The other copy is closed at once. A dictation in progress is lost.",
            ChooseVersionWindow.ForcedCloseText(null));
    }
}
