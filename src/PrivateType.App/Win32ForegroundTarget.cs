using System.Globalization;
using PrivateType.Core;

namespace PrivateType.App;

internal sealed class Win32ForegroundTarget : IForegroundTarget
{
    public DictationTarget Capture()
    {
        return ToTarget(NativeMethods.GetForegroundWindow());
    }

    public TargetEligibility GetEligibility(DictationTarget target)
    {
        if (!TryGetHandle(target, out var handle) || !NativeMethods.IsWindow(handle))
            return TargetEligibility.Invalid;

        if (NativeMethods.GetWindowThreadProcessId(handle, out var processId) == 0)
            return TargetEligibility.Invalid;

        if (NativeMethods.GetForegroundWindow() != handle)
            return TargetEligibility.Changed;

        return WindowsProcessIntegrity.CanReceiveInputFromCurrentProcess(processId)
            ? TargetEligibility.Eligible
            : TargetEligibility.Ineligible;
    }

    internal static bool TryGetHandle(DictationTarget target, out nint handle)
    {
        handle = long.TryParse(target.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? (nint)value : nint.Zero;
        return handle != nint.Zero;
    }

    private static DictationTarget ToTarget(nint handle)
    {
        return handle == nint.Zero
            ? DictationTarget.None
            : new DictationTarget(handle.ToInt64().ToString(CultureInfo.InvariantCulture));
    }
}
