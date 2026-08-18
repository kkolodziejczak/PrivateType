using System.Runtime.InteropServices;

namespace PrivateType.App;

internal static class WindowsTaskbarIdentity
{
    internal const string AppUserModelId = "PrivateType.PrivateType";

    internal static void Apply()
        => Apply(SetCurrentProcessExplicitAppUserModelID);

    internal static void Apply(Func<string, int> setIdentity)
    {
        var result = setIdentity(AppUserModelId);
        Marshal.ThrowExceptionForHR(result);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string applicationId);
}
