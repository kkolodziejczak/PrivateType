namespace PrivateType.Core;

public sealed class ForegroundTargetGuard(IForegroundTarget foregroundTarget)
{
    private DictationTarget capturedTarget = DictationTarget.None;

    public void Capture()
    {
        capturedTarget = foregroundTarget.Capture();
    }

    public TargetEligibility GetEligibility()
    {
        if (capturedTarget == DictationTarget.None)
            return TargetEligibility.Invalid;

        return foregroundTarget.GetEligibility(capturedTarget);
    }
}
