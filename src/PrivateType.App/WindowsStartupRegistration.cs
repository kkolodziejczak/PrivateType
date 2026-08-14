using Microsoft.Win32;

namespace PrivateType.App;

internal sealed class WindowsStartupRegistration
{
    private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    private const string ValueName = "PrivateType";

    public void Apply(bool enabled, string executablePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Windows Startup settings are unavailable for this user.");

        if (enabled)
        {
            key.SetValue(ValueName, Quote(executablePath), RegistryValueKind.String);
            return;
        }

        key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    internal static string Quote(string executablePath) => $"\"{executablePath}\"";
}
