using System.Windows;

namespace PrivateType.App;

public partial class App : System.Windows.Application
{
    private DictationApplication? application;

    protected override void OnStartup(StartupEventArgs e)
    {
        WindowsTaskbarIdentity.Apply();
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        application = new DictationApplication();
        application.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        application?.Dispose();
        base.OnExit(e);
    }
}
