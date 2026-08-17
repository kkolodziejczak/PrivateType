using System.Windows;
using System.Windows.Media.Animation;

namespace PrivateType.App;

public partial class ModelSetupWindow : Window
{
    internal const string MissingEngineHeading = "Local speech engine missing";
    internal const string MissingEngineStatus = "This copy of PrivateType does not include the local speech engine.\n\nDownload and extract the complete PrivateType portable ZIP, or configure the engine when running from source.";
    internal const string EngineCouldNotStartHeading = "Local speech engine could not start";
    internal const string EngineCouldNotStartStatus = "The local speech engine is present but could not start.\n\nInstall the Microsoft Visual C++ Redistributable (x64), then select Retry.";
    private bool completed;

    public ModelSetupWindow()
    {
        InitializeComponent();
        Closing += (_, _) => { if (!completed) CancelRequested?.Invoke(); };
    }

    public event Action? RetryRequested;
    public event Action? CancelRequested;
    public event Action? DownloadRequested;

    public void ShowMissingEnginePrerequisite()
    {
        HeadingText.Text = MissingEngineHeading;
        StatusText.Text = MissingEngineStatus;
        ShowPrerequisiteActions();
    }

    public void ShowEngineStartPrerequisite()
    {
        HeadingText.Text = EngineCouldNotStartHeading;
        StatusText.Text = EngineCouldNotStartStatus;
        ShowPrerequisiteActions();
    }

    private void ShowPrerequisiteActions()
    {
        ConsentPanel.Visibility = Visibility.Collapsed;
        ProgressBar.Visibility = Visibility.Collapsed;
        ProgressCaption.Text = "The model will not download until the local speech engine is ready.";
        DownloadButton.Visibility = Visibility.Collapsed;
        RetryButton.Visibility = Visibility.Visible;
    }

    public void ShowDownloadConsent()
    {
        HeadingText.Text = "Download local dictation";
        StatusText.Text = "Download the local speech model before using dictation.";
        ConsentPanel.Visibility = Visibility.Visible;
        ProgressBar.Visibility = Visibility.Collapsed;
        ProgressCaption.Text = string.Empty;
        DownloadButton.Visibility = Visibility.Visible;
        DownloadButton.IsEnabled = TermsCheckBox.IsChecked == true;
        RetryButton.Visibility = Visibility.Collapsed;
    }

    public void ShowProgress(long downloaded, long total)
    {
        HeadingText.Text = "Preparing local dictation";
        StatusText.Text = "Downloading and verifying the local speech model…";
        ConsentPanel.Visibility = Visibility.Collapsed;
        DownloadButton.Visibility = Visibility.Collapsed;
        ProgressBar.Visibility = Visibility.Visible;
        ProgressCaption.Text = $"{downloaded / 1024d / 1024d:F0} MB of {total / 1024d / 1024d:F0} MB";
        ProgressBar.Value = total == 0 ? 0 : Math.Min(100, downloaded * 100d / total);
        ProgressBar.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.75, TimeSpan.FromSeconds(0.8)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
    }

    public void ShowFailure(string message)
    {
        StatusText.Text = $"Setup failed: {message}";
        ProgressCaption.Text = string.Empty;
        ProgressBar.BeginAnimation(OpacityProperty, null);
        ProgressBar.Opacity = 1;
        RetryButton.Visibility = Visibility.Visible;
        DownloadButton.Visibility = Visibility.Collapsed;
        CancelButton.Content = "Close";
    }

    public void CloseAfterSuccess() { completed = true; Close(); }
    private void Retry(object sender, RoutedEventArgs e) => RetryRequested?.Invoke();
    private void Download(object sender, RoutedEventArgs e) => DownloadRequested?.Invoke();
    private void TermsChanged(object sender, RoutedEventArgs e) => DownloadButton.IsEnabled = TermsCheckBox.IsChecked == true;
    private void Cancel(object sender, RoutedEventArgs e) => CancelRequested?.Invoke();
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
    private void DragWindow(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove(); }
}
