using System.Diagnostics;
using System.IO;
using System.Windows;

namespace PrivateType.App;

public partial class OpenSourceLicensesWindow : Window
{
    private readonly string licensesDirectory;

    public OpenSourceLicensesWindow() : this(Path.Combine(AppContext.BaseDirectory, "licenses"))
    {
    }

    // A portable release ships its notices in the licenses folder beside the app.
    internal OpenSourceLicensesWindow(string licensesDirectory)
    {
        InitializeComponent();
        this.licensesDirectory = licensesDirectory;
        Notices.Text = ReadNotices();
    }

    private string ReadNotices()
    {
        var noticePath = Path.Combine(licensesDirectory, "THIRD-PARTY-NOTICES.txt");
        return File.Exists(noticePath)
            ? File.ReadAllText(noticePath)
            : "PrivateType is MIT-licensed. A portable release includes the complete third-party notices and license texts in its licenses folder. Build a portable release to inspect the complete bundle.";
    }

    private void OpenLicensesFolder(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(licensesDirectory))
            Process.Start(new ProcessStartInfo(licensesDirectory) { UseShellExecute = true });
    }

    private void Close(object sender, RoutedEventArgs e) => Close();
}
