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
        var resource = System.Windows.Application.GetResourceStream(ResourceUri(fileName))
            ?? throw new InvalidOperationException($"The tray icon resource '{fileName}' was not found.");
        using var stream = resource.Stream;
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }

    internal static Uri ResourceUri(string fileName) =>
        new($"/{typeof(TrayIconSet).Assembly.GetName().Name};component/Assets/{fileName}", UriKind.Relative);
}
