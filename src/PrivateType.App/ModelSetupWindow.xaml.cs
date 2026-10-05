using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using PrivateType.Core;

namespace PrivateType.App;

public partial class ModelSetupWindow : Window
{
    internal const string MissingEngineHeading = "Local speech engine missing";
    internal const string MissingEngineStatus = "This copy of PrivateType does not include the local speech engine.\n\nDownload and extract the complete PrivateType portable ZIP, or configure the engine when running from source.";
    internal const string EngineCouldNotStartHeading = "Local speech engine could not start";
    internal const string EngineCouldNotStartStatus = "The local speech engine is present but could not start.\n\nInstall the Microsoft Visual C++ Redistributable (x64), then select Retry.";
    internal const string SharedStorageNotice = "Default: this verified model is shared with cache-aware PrivateType versions for this Windows account. Existing older release folders are not moved or deleted.";
    internal const string PortableStorageNotice = "Portable-local mode: this copy uses its existing app\\models folder and does not use the shared cache.";
    private SpeechModelDefinition chosenModel;
    private bool completed;

    public ModelSetupWindow() : this(SpeechModelCatalog.DefaultId)
    {
    }

    internal ModelSetupWindow(string chosenModelId)
    {
        InitializeComponent();
        chosenModel = SpeechModelCatalog.Get(SpeechModelCatalog.IsSupported(chosenModelId) ? chosenModelId : SpeechModelCatalog.DefaultId);
        // The recommended model comes first.
        ModelChoices.ItemsSource = SpeechModelCatalog.All
            .OrderByDescending(model => model.Id == SpeechModelCatalog.DefaultId)
            .Select(model => new SetupModelChoice(model, model == chosenModel, Choose))
            .ToArray();
        ShowChosenTerms();
        StorageNoticeText.Text = StorageNotice(ModelStorageMode.Shared);
        Closing += (_, _) => { if (!completed) CancelRequested?.Invoke(); };
    }

    internal SpeechModelDefinition ChosenModel => chosenModel;

    private void Choose(SpeechModelDefinition model)
    {
        chosenModel = model;
        ShowChosenTerms();
    }

    // Consent is per model: changing the choice clears the terms checkbox.
    private void ShowChosenTerms()
    {
        var model = ChosenModel;
        ModelTermsName.Text = $"{model.LicenseName} model terms: ";
        ModelTermsLink.NavigateUri = model.LicenseUri;
        ModelTermsLinkText.Text = model.LicenseUri.Host + model.LicenseUri.AbsolutePath.TrimEnd('/');
        TermsText.Text = $"I understand that this downloads a separate local model under {model.LicenseName} terms.";
        TermsCheckBox.IsChecked = false;
    }

    internal static string StorageNotice(ModelStorageMode mode)
        => mode == ModelStorageMode.Portable ? PortableStorageNotice : SharedStorageNotice;

    internal void SetStorageMode(ModelStorageMode mode)
        => StorageNoticeText.Text = StorageNotice(mode);

    internal static ProcessStartInfo ModelTermsBrowserStartInfo(SpeechModelDefinition? model = null)
        => new((model ?? SpeechModelCatalog.Nemotron).LicenseUri.AbsoluteUri) { UseShellExecute = true };

    public event Action? RetryRequested;
    public event Action? CancelRequested;
    public event Action<SpeechModelDefinition>? DownloadRequested;

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
        SetStorageMode(storageMode);
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
        SetStorageMode(storageMode);
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
        StatusText.Text = $"Downloading and verifying {ChosenModel.DisplayName}…";
        ConsentPanel.Visibility = Visibility.Collapsed;
        DownloadButton.Visibility = Visibility.Collapsed;
        ProgressBar.Visibility = Visibility.Visible;
        SetStorageMode(storageMode);
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
    private void Download(object sender, RoutedEventArgs e) => DownloadRequested?.Invoke(ChosenModel);
    private void TermsChanged(object sender, RoutedEventArgs e) => DownloadButton.IsEnabled = TermsCheckBox.IsChecked == true;
    private void OpenModelTerms(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(ModelTermsBrowserStartInfo(ChosenModel));
        e.Handled = true;
    }
    private void Cancel(object sender, RoutedEventArgs e) => CancelRequested?.Invoke();
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
    private void DragWindow(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove(); }
}

internal sealed class SetupModelChoice(SpeechModelDefinition model, bool isChosen, Action<SpeechModelDefinition> chosen) : INotifyPropertyChanged
{
    private bool isChosen = isChosen;

    public event PropertyChangedEventHandler? PropertyChanged;

    public SpeechModelDefinition Model { get; } = model;
    public string Name => Model.DisplayName;
    public string Summary => Model.Summary;
    public bool IsRecommended => Model.Id == SpeechModelCatalog.DefaultId;
    public string Details => $"{SpeechModelCatalog.SizeLabel(Model)}. Downloaded from NVIDIA on Hugging Face and kept on this computer.";

    public bool IsChosen
    {
        get => isChosen;
        set
        {
            if (isChosen == value)
                return;
            isChosen = value;
            Notify();
            if (value)
                chosen(Model);
        }
    }

    private void Notify([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
