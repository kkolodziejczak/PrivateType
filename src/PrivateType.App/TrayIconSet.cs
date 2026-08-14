using System.Drawing;

namespace PrivateType.App;

internal sealed class TrayIconSet : IDisposable
{
    public TrayIconSet()
    {
        Ready = Load("PrivateType.ready.ico");
        Listening = Load("PrivateType.listening.ico");
    }

    public Icon Ready { get; }
    public Icon Listening { get; }

    public void Dispose()
    {
        Ready.Dispose();
        Listening.Dispose();
    }

    private static Icon Load(string fileName)
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri($"pack://application:,,,/PrivateType.App;component/Assets/{fileName}"))
            ?? throw new InvalidOperationException($"The tray icon resource '{fileName}' was not found.");
        using var stream = resource.Stream;
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }
}
