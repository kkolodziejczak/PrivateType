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
    // Each stopped state: a title for what happened, one line of why, and one line of what to do.
    internal const string MissingEngineHeading = "Speech engine missing";
    internal const string MissingEngineCause = "This copy of PrivateType came without its speech engine.";
    internal const string MissingEngineStep = "Download the complete PrivateType portable ZIP, extract every file, and start PrivateType from that folder.";
    internal const string EngineCouldNotStartHeading = "Speech engine couldn't start";
    internal const string EngineCouldNotStartCause = "The speech engine is here, but Windows couldn't start it.";
    internal const string EngineCouldNotStartStep = "Install the Microsoft Visual C++ Redistributable (x64), then select Retry.";
    internal const string FailureHeading = "Download interrupted";
    internal const string DroppedConnectionCause = "The connection dropped before the model finished. Retry starts the download again.";
    internal const string UnfinishedDownloadCause = "The model didn't finish downloading. Retry starts the download again.";
    internal const string SourceNotice = "From NVIDIA on Hugging Face.";
    internal const string ChangeLaterNotice = "Switch or delete models later in Settings → Model.";
    internal const string SharedStorageNotice = "Stays on this computer, shared with other PrivateType versions on this Windows account.";
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
        ModelTermsLink.NavigateUri = model.LicenseUri;
        ModelTermsLinkText.Text = $"Read the {model.LicenseName} terms";
        TermsText.Text = $"I accept the {model.LicenseName} terms for {ModelLibraryEditor.ShortName(model)}";
        TermsCheckBox.IsChecked = false;
        ShowDownloadReadiness();
    }

    internal static string StorageNotice(ModelStorageMode mode)
        => mode == ModelStorageMode.Portable ? PortableStorageNotice : SharedStorageNotice;

    // Where the model comes from and where it stays, in one note.
    internal void SetStorageMode(ModelStorageMode mode)
        => StorageNoticeText.Text = $"{SourceNotice} {StorageNotice(mode)} {ChangeLaterNotice}";

    internal static ProcessStartInfo ModelTermsBrowserStartInfo(SpeechModelDefinition? model = null)
        => new((model ?? SpeechModelCatalog.Nemotron).LicenseUri.AbsoluteUri) { UseShellExecute = true };

    public event Action? RetryRequested;
    public event Action? CancelRequested;
    public event Action<SpeechModelDefinition>? DownloadRequested;

    public void ShowMissingEnginePrerequisite() => ShowMissingEnginePrerequisite(ModelStorageMode.Shared);

    internal void ShowMissingEnginePrerequisite(ModelStorageMode storageMode)
    {
        ShowStopped(MissingEngineHeading, MissingEngineCause, MissingEngineStep);
        ShowPrerequisiteActions(storageMode);
    }

    public void ShowEngineStartPrerequisite() => ShowEngineStartPrerequisite(ModelStorageMode.Shared);

    internal void ShowEngineStartPrerequisite(ModelStorageMode storageMode)
    {
        ShowStopped(EngineCouldNotStartHeading, EngineCouldNotStartCause, EngineCouldNotStartStep);
        ShowPrerequisiteActions(storageMode);
    }

    private void ShowPrerequisiteActions(ModelStorageMode storageMode)
    {
        SetStorageMode(storageMode);
        ConsentPanel.Visibility = Visibility.Collapsed;
        ProgressBar.Visibility = Visibility.Collapsed;
        PhaseStrip.Visibility = Visibility.Collapsed;
        DownloadButton.Visibility = Visibility.Collapsed;
        RetryButton.Visibility = Visibility.Visible;
        ShowDownloadReadiness();
    }

    public void ShowDownloadConsent() => ShowDownloadConsent(ModelStorageMode.Shared);

    internal void ShowDownloadConsent(ModelStorageMode storageMode)
    {
        HeadingText.Text = "Download local dictation";
        StatusText.Text = "Download the local speech model before using dictation.";
        NextStepText.Text = string.Empty;
        SetStorageMode(storageMode);
        ConsentPanel.Visibility = Visibility.Visible;
        ProgressBar.Visibility = Visibility.Collapsed;
        PhaseStrip.Visibility = Visibility.Collapsed;
        DownloadButton.Visibility = Visibility.Visible;
        RetryButton.Visibility = Visibility.Collapsed;
        ShowDownloadReadiness();
    }

    public void ShowProgress(long downloaded, long total)
        => ShowProgress(downloaded, total, ModelStorageMode.Shared);

    internal void ShowProgress(long downloaded, long total, ModelStorageMode storageMode)
    {
        // One phase at a time: the download counts up, then the finished file is checked before first use.
        var verifying = total > 0 && downloaded >= total;
        HeadingText.Text = verifying ? "Verifying the download…" : $"Downloading {downloaded / 1024d / 1024d:F0} of {total / 1024d / 1024d:F0} MiB";
        StatusText.Text = verifying
            ? $"{ChosenModel.DisplayName} · ready to dictate once it's checked."
            : $"{ChosenModel.DisplayName} · checked next, then ready to dictate.";
        NextStepText.Text = string.Empty;
        ConsentPanel.Visibility = Visibility.Collapsed;
        DownloadButton.Visibility = Visibility.Collapsed;
        ShowDownloadReadiness();
        SetStorageMode(storageMode);
        // A full bar would claim progress that checking doesn't have; the phase strip says which step is under way instead.
        PhaseStrip.Visibility = verifying ? Visibility.Visible : Visibility.Collapsed;
        ProgressBar.Visibility = verifying ? Visibility.Collapsed : Visibility.Visible;
        ProgressBar.Value = total == 0 ? 0 : Math.Min(100, downloaded * 100d / total);
        ProgressBar.BeginAnimation(OpacityProperty, verifying ? null
            : new DoubleAnimation(1, 0.75, TimeSpan.FromSeconds(0.8)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
    }

    // A dropped connection is told apart from other failures by the network exceptions in the chain.
    internal static bool IsDroppedConnection(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is System.Net.Sockets.SocketException or System.Net.Http.HttpRequestException or System.Net.Http.HttpIOException)
                return true;
        }

        return false;
    }

    public void ShowFailure(Exception exception) => ShowFailure(exception.Message, IsDroppedConnection(exception));

    // The plain cause and what Retry does lead; the system's message follows as a quieter, selectable detail.
    internal void ShowFailure(string message, bool connectionDropped)
    {
        // The partial download is discarded, so the bar goes with it.
        ShowStopped(FailureHeading, connectionDropped ? DroppedConnectionCause : UnfinishedDownloadCause, string.Empty);
        FailureDetailText.Text = message;
        FailureDetailText.Visibility = Visibility.Visible;
        ProgressBar.BeginAnimation(OpacityProperty, null);
        ProgressBar.Visibility = Visibility.Collapsed;
        PhaseStrip.Visibility = Visibility.Collapsed;
        RetryButton.Visibility = Visibility.Visible;
        DownloadButton.Visibility = Visibility.Collapsed;
        ShowDownloadReadiness();
        CancelButton.Content = "Close";
    }

    private void ShowStopped(string heading, string cause, string step)
    {
        FailureDetailText.Visibility = Visibility.Collapsed;
        HeadingText.Text = heading;
        StatusText.Text = cause;
        NextStepText.Text = step;
    }

    public void CloseAfterSuccess() { completed = true; Close(); }
    private void Retry(object sender, RoutedEventArgs e) => RetryRequested?.Invoke();
    private void Download(object sender, RoutedEventArgs e) => DownloadRequested?.Invoke(ChosenModel);
    private void TermsChanged(object sender, RoutedEventArgs e) => ShowDownloadReadiness();

    // Download waits for the ticked terms, and says so beside the button while it waits.
    private void ShowDownloadReadiness()
    {
        DownloadButton.IsEnabled = TermsCheckBox.IsChecked == true;
        DownloadReasonText.Visibility = DownloadButton.Visibility == Visibility.Visible && !DownloadButton.IsEnabled ? Visibility.Visible : Visibility.Hidden;
    }
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
    // The name Settings uses for the same model; the full ID is its secondary line.
    public string ShortName => ModelLibraryEditor.ShortName(Model);
    // One description per model, the same three facts for each: how text appears, which language, and phrase boosting.
    public string Headline => Model.Id switch
    {
        SpeechModelCatalog.ParakeetId => "Text appears when you release, with an optional preview while you speak · detects the language itself, 25 European languages · no phrase boosting, fixes still apply",
        SpeechModelCatalog.NemotronId => "Words appear while you speak · uses each shortcut's language, 32 languages including Asian ones · phrase boosting",
        _ => Model.Summary
    };
    public bool IsRecommended => Model.Id == SpeechModelCatalog.DefaultId;
    // The download size as a table value: "681 MiB".
    public string Size => $"{Model.Manifest.ExpectedBytes / 1024d / 1024d:F0} MiB";

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
