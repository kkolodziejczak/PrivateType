using System.Diagnostics;
using System.IO;
using System.Windows;

namespace PrivateType.App;

public partial class OpenSourceLicensesWindow : Window
{
    public OpenSourceLicensesWindow()
    {
        InitializeComponent();
        Notices.Text = ReadNotices();
    }

    private static string ReadNotices()
    {
        var noticePath = Path.Combine(AppContext.BaseDirectory, "licenses", "THIRD-PARTY-NOTICES.txt");
        return File.Exists(noticePath)
            ? File.ReadAllText(noticePath)
            : "PrivateType is MIT-licensed. A portable release includes the complete third-party notices and license texts in its licenses folder. Build a portable release to inspect the complete bundle.";
    }

    private void OpenLicensesFolder(object sender, RoutedEventArgs e)
    {
        var licensesDirectory = Path.Combine(AppContext.BaseDirectory, "licenses");
        if (Directory.Exists(licensesDirectory))
            Process.Start(new ProcessStartInfo(licensesDirectory) { UseShellExecute = true });
    }

    private void Close(object sender, RoutedEventArgs e) => Close();
}
