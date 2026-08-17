using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PrivateType.Launcher;

internal static class Program
{
    private const uint ErrorIcon = 0x00000010;

    public static int Main()
    {
        var applicationDirectory = Path.Combine(AppContext.BaseDirectory, "app");
        var applicationPath = Path.Combine(applicationDirectory, "PrivateType.exe");

        if (!File.Exists(applicationPath))
        {
            ShowError("The PrivateType application files are missing. Extract the complete ZIP before starting PrivateType.");
            return 1;
        }

        try
        {
            var startInfo = new ProcessStartInfo(applicationPath)
            {
                UseShellExecute = false,
                WorkingDirectory = applicationDirectory
            };

            foreach (var argument in Environment.GetCommandLineArgs().Skip(1))
            {
                startInfo.ArgumentList.Add(argument);
            }

            Process.Start(startInfo);
            return 0;
        }
        catch (Exception exception)
        {
            ShowError($"PrivateType could not be started.\n\n{exception.Message}");
            return 1;
        }
    }

    private static void ShowError(string message) =>
        MessageBox(IntPtr.Zero, message, "PrivateType", ErrorIcon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(IntPtr window, string text, string caption, uint type);
}
