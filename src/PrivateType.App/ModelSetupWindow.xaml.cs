using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Navigation;

namespace PrivateType.App;

public partial class ModelSetupWindow : Window
{
    internal static readonly Uri ModelTermsUri = new("https://openmdw.ai/license/1-1/");
    internal const string MissingEngineHeading = "Local speech engine missing";
    internal const string MissingEngineStatus = "This copy of PrivateType does not include the local speech engine.\n\nDownload and extract the complete PrivateType portable ZIP, or configure the engine when running from source.";
    internal const string EngineCouldNotStartHeading = "Local speech engine could not start";
    internal const string EngineCouldNotStartStatus = "The local speech engine is present but could not start.\n\nInstall the Microsoft Visual C++ Redistributable (x64), then select Retry.";
    internal const string SharedStorageNotice = "Default: this verified model is shared with cache-aware PrivateType versions for this Windows account. Existing older release folders are not moved or deleted.";
    internal const string PortableStorageNotice = "Portable-local mode: this copy uses its existing app\\models folder and does not use the shared cache.";
    private bool completed;

    public ModelSetupWindow()
    {
        InitializeComponent();
        ModelTermsLink.NavigateUri = ModelTermsUri;
        StorageNoticeText.Text = StorageNotice(ModelStorageMode.Shared);
        Closing += (_, _) => { if (!completed) CancelRequested?.Invoke(); };
    }

    internal static string StorageNotice(ModelStorageMode mode)
        => mode == ModelStorageMode.Portable ? PortableStorageNotice : SharedStorageNotice;

    internal static ProcessStartInfo ModelTermsBrowserStartInfo()
        => new(ModelTermsUri.AbsoluteUri) { UseShellExecute = true };

    public event Action? RetryRequested;
    public event Action? CancelRequested;
    public event Action? DownloadRequested;

    public void ShowMissingEnginePrerequisite() => ShowMissingEnginePrerequisite(ModelStorageMode.Shared);

    internal void ShowMissingEnginePrerequisite(ModelStorageMode storageMode)
    {
        HeadingText.Text = MissingEngineHeading;
        StatusText.Text = MissingEngineStatus;
        ShowPrerequisiteActions(storageMode);
    }

    public void ShowEngineStartPrerequisite() => ShowEngineStartPrerequisite(ModelStorageMode.Shared);

    internal void ShowEngineStartPrerequisite(ModelStorageMode storageMode)
    {
        HeadingText.Text = EngineCouldNotStartHeading;
        StatusText.Text = EngineCouldNotStartStatus;
        ShowPrerequisiteActions(storageMode);
    }

    private void ShowPrerequisiteActions(ModelStorageMode storageMode)
    {
        StorageNoticeText.Text = StorageNotice(storageMode);
        ConsentPanel.Visibility = Visibility.Collapsed;
        ProgressBar.Visibility = Visibility.Collapsed;
        ProgressCaption.Text = "The model will not download until the local speech engine is ready.";
        DownloadButton.Visibility = Visibility.Collapsed;
        RetryButton.Visibility = Visibility.Visible;
    }

    public void ShowDownloadConsent() => ShowDownloadConsent(ModelStorageMode.Shared);

    internal void ShowDownloadConsent(ModelStorageMode storageMode)
    {
        HeadingText.Text = "Download local dictation";
        StatusText.Text = "Download the local speech model before using dictation.";
        StorageNoticeText.Text = StorageNotice(storageMode);
        ConsentPanel.Visibility = Visibility.Visible;
        ProgressBar.Visibility = Visibility.Collapsed;
        ProgressCaption.Text = string.Empty;
        DownloadButton.Visibility = Visibility.Visible;
        DownloadButton.IsEnabled = TermsCheckBox.IsChecked == true;
        RetryButton.Visibility = Visibility.Collapsed;
    }

    public void ShowProgress(long downloaded, long total)
        => ShowProgress(downloaded, total, ModelStorageMode.Shared);

    internal void ShowProgress(long downloaded, long total, ModelStorageMode storageMode)
    {
        HeadingText.Text = "Preparing local dictation";
        StatusText.Text = "Downloading and verifying the local speech model…";
        ConsentPanel.Visibility = Visibility.Collapsed;
        DownloadButton.Visibility = Visibility.Collapsed;
        ProgressBar.Visibility = Visibility.Visible;
        StorageNoticeText.Text = StorageNotice(storageMode);
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
    private void OpenModelTerms(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(ModelTermsBrowserStartInfo());
        e.Handled = true;
    }
    private void Cancel(object sender, RoutedEventArgs e) => CancelRequested?.Invoke();
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
    private void DragWindow(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove(); }
}
