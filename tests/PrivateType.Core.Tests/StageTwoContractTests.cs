using PrivateType.Core;
using Xunit;

namespace PrivateType.Core.Tests;

public sealed class StageTwoContractTests
{
    [Fact]
    public void Exposes_a_dedicated_session_owner_for_one_dictation_hold()
    {
        var sessionType = typeof(CommitCoordinator).Assembly.GetType("PrivateType.Core.DictationSession");

        Assert.NotNull(sessionType);
    }

    [Fact]
    public void Exposes_a_dedicated_target_guard_owner()
    {
        var guardType = typeof(CommitCoordinator).Assembly.GetType("PrivateType.Core.ForegroundTargetGuard");

        Assert.NotNull(guardType);
    }
}
