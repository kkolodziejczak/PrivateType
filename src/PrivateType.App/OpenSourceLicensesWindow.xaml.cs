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
        ShowNotices();
    }

    private void ShowNotices()
    {
        var noticePath = Path.Combine(licensesDirectory, "THIRD-PARTY-NOTICES.txt");
        var found = File.Exists(noticePath);
        Notices.Text = found ? File.ReadAllText(noticePath) : string.Empty;
        Notices.Visibility = found ? Visibility.Visible : Visibility.Collapsed;
        MissingNoticesPanel.Visibility = found ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OpenLicensesFolder(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(licensesDirectory))
            Process.Start(new ProcessStartInfo(licensesDirectory) { UseShellExecute = true });
    }

    private void Close(object sender, RoutedEventArgs e) => Close();
}
