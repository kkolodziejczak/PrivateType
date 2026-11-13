using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Navigation;
using PrivateType.Core;

namespace PrivateType.App;

// Setup is a short journey: choose a model, review and download it, then wait while it is checked.
// One fixed frame shows one step at a time; the rail says where the person is and what is left.
public partial class ModelSetupWindow : Window
{
    // Each stopped state: a title for what happened, one line of why, and one line of what to do.
    internal const string MissingEngineHeading = "Speech engine missing";
    internal const string MissingEngineCause = "This copy of PrivateType came without its speech engine.";
    internal const string MissingEngineStep = $"{ProjectLinks.GetCompleteZipAction}, extract every file, and start PrivateType from that folder.";
    internal const string EngineCouldNotStartHeading = "Speech engine couldn't start";
    internal const string EngineCouldNotStartCause = "The speech engine is here, but Windows couldn't start it.";
    internal const string EngineCouldNotStartStep = $"Install the Microsoft Visual C++ Redistributable (x64), then select {CheckAgainAction}.";
    // Buttons name their result: a missing or stopped engine is looked for again, a failed download starts again, and leaving setup quits the app.
    internal const string CheckAgainAction = "Check again";
    internal const string DownloadAgainAction = "Download again";
    internal const string QuitAction = "Quit PrivateType";
    internal const string FailureHeading = "Download interrupted";
    internal const string DroppedConnectionCause = "The connection dropped before the model finished downloading.";
    internal const string UnfinishedDownloadCause = "The model didn't finish downloading.";
    internal const string StartsOverNotice = "Nothing was kept, so the download starts from the beginning.";
    // The whole file arrived but isn't the published model: it failed the check, step 3.
    internal const string CheckFailedHeading = "The download didn't pass the check";
    internal const string CheckFailedCause = "The file arrived, but it doesn't match the published model, so it wasn't used.";
    // A full disk: downloading again can't help until there is room.
    internal const string DiskFullHeading = "Not enough free space";
    internal const string DiskFullStep = "Free up space, then download again.";
    internal const string CheckingFilesName = "Checking the files";
    internal const string SharedStorageNotice = "The model files stay on this computer and are shared by PrivateType versions on this Windows account.";
    internal const string PortableStorageNotice = "The model files stay in this copy's app\\models folder and aren't shared with other PrivateType versions.";
    private const int JourneySteps = 3;
    private SpeechModelDefinition chosenModel;
    private SetupView? shownView;
    private SetupFailure failure;
    private string failureCause = string.Empty;
    private bool completed;

    public ModelSetupWindow() : this(SpeechModelCatalog.DefaultId)
    {
    }

    internal ModelSetupWindow(string chosenModelId)
    {
        InitializeComponent();
        chosenModel = SpeechModelCatalog.Get(SpeechModelCatalog.IsSupported(chosenModelId) ? chosenModelId : SpeechModelCatalog.DefaultId);
        // The recommended model comes first, and the order never changes.
        ModelChoices.ItemsSource = SpeechModelCatalog.All
            .OrderByDescending(model => model.Id == SpeechModelCatalog.DefaultId)
            .Select(model => new SetupModelChoice(model, model == chosenModel, Choose))
            .ToArray();
        QuitButton.Content = QuitAction;
        CheckAgainButton.Content = CheckAgainAction;
        GetZipButton.Content = ProjectLinks.GetCompleteZipAction;
        VisualCppLink.Content = SystemLinks.GetVisualCppRedistributableAction;
        CheckingText.SetValue(AutomationProperties.NameProperty, CheckingFilesName);
        DownloadAgainButton.Content = DownloadAgainAction;
        ShowChosenModel();
        SetStorageMode(ModelStorageMode.Shared);
        ShowView(SetupView.Choose);
        Closing += (_, _) => { if (!completed) CancelRequested?.Invoke(); };
    }

    internal SpeechModelDefinition ChosenModel => chosenModel;

    public event Action? RetryRequested;
    public event Action? CancelRequested;
    public event Action<SpeechModelDefinition>? DownloadRequested;

    private void Choose(SpeechModelDefinition model)
    {
        chosenModel = model;
        // Consent is per model: changing the choice clears the terms checkbox.
        TermsCheckBox.IsChecked = false;
        ShowChosenModel();
    }

    // Everything that names the chosen model: the buttons, the review, and the terms.
    private void ShowChosenModel()
    {
        var model = ChosenModel;
        var name = ModelLibraryEditor.ShortName(model);
        ContinueButton.Content = $"Continue with {name}";
        DownloadButton.Content = $"Download {name}";
        SizeText.Text = SetupModelChoice.SizeOf(model);
        LicenseNameText.Text = model.LicenseName;
        ModelTermsLink.NavigateUri = model.LicenseUri;
        ModelTermsLink.ToolTip = $"Open the {model.LicenseName} terms in your default browser";
        ModelIdText.Text = model.DisplayName;
        ProgressModelText.Text = model.DisplayName;
        TermsText.Text = $"I accept the {model.LicenseName} terms for {name}";
        ShowDownloadReadiness();
    }

    internal static string StorageNotice(ModelStorageMode mode)
        => mode == ModelStorageMode.Portable ? PortableStorageNotice : SharedStorageNotice;

    internal void SetStorageMode(ModelStorageMode mode) => StorageNoticeText.Text = StorageNotice(mode);

    internal static ProcessStartInfo ModelTermsBrowserStartInfo(SpeechModelDefinition? model = null)
        => new((model ?? SpeechModelCatalog.Nemotron).LicenseUri.AbsoluteUri) { UseShellExecute = true };

    public void ShowMissingEnginePrerequisite() => ShowMissingEnginePrerequisite(ModelStorageMode.Shared);

    internal void ShowMissingEnginePrerequisite(ModelStorageMode storageMode)
    {
        SetStorageMode(storageMode);
        ShowView(SetupView.EngineMissing);
    }

    public void ShowEngineStartPrerequisite() => ShowEngineStartPrerequisite(ModelStorageMode.Shared);

    internal void ShowEngineStartPrerequisite(ModelStorageMode storageMode)
    {
        SetStorageMode(storageMode);
        ShowView(SetupView.EngineCouldNotStart);
    }

    public void ShowDownloadConsent() => ShowDownloadConsent(ModelStorageMode.Shared);

    // Setup opens on the first step, with the model already chosen.
    internal void ShowDownloadConsent(ModelStorageMode storageMode)
    {
        SetStorageMode(storageMode);
        ShowView(SetupView.Choose);
    }

    public void ShowProgress(long downloaded, long total)
        => ShowProgress(downloaded, total, ModelStorageMode.Shared);

    internal void ShowProgress(long downloaded, long total, ModelStorageMode storageMode)
    {
        SetStorageMode(storageMode);
        // One phase at a time: the download counts up, then the finished file is checked before first use.
        var checking = total > 0 && downloaded >= total;
        ShowView(checking ? SetupView.Checking : SetupView.Downloading);
        ProgressAmountText.Text = $"{Mebibytes(downloaded)} of {Mebibytes(total)} MiB";
        // Checking has no measurable progress, so it has no bar: what is done, with a tick, and what is under way, in words.
        DownloadedText.Text = $"{Mebibytes(total)} MiB downloaded";
        ProgressBar.Value = total == 0 ? 0 : Math.Min(100, downloaded * 100d / total);
        ProgressBar.SetValue(AutomationProperties.NameProperty, checking ? CheckingFilesName : ProgressAmountText.Text);
    }

    private static string Mebibytes(long bytes) => $"{bytes / 1024d / 1024d:F0}";

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

    // The provisioner throws InvalidDataException for a file that doesn't match the pinned size and hash.
    internal static SetupFailure Classify(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is InvalidDataException)
                return SetupFailure.CheckFailed;
            if (current is IOException { HResult: DiskFullHResult or HandleDiskFullHResult })
                return SetupFailure.DiskFull;
        }

        return IsDroppedConnection(exception) ? SetupFailure.ConnectionDropped : SetupFailure.Unfinished;
    }

    // ERROR_DISK_FULL and ERROR_HANDLE_DISK_FULL as HRESULTs.
    private const int DiskFullHResult = unchecked((int)0x80070070);
    private const int HandleDiskFullHResult = unchecked((int)0x80070027);

    // The drive is where the model is kept ("C:"), named when the disk is full.
    public void ShowFailure(Exception exception, string? drive = null) => ShowFailure(exception.Message, Classify(exception), drive);

    internal void ShowFailure(string message, bool connectionDropped)
        => ShowFailure(message, connectionDropped ? SetupFailure.ConnectionDropped : SetupFailure.Unfinished);

    // The plain cause and what Download again does lead; the system's message follows as a quieter, selectable detail.
    internal void ShowFailure(string message, SetupFailure kind, string? drive = null)
    {
        failure = kind;
        var size = SetupModelChoice.SizeOf(ChosenModel);
        failureCause = kind switch
        {
            SetupFailure.ConnectionDropped => DroppedConnectionCause,
            SetupFailure.CheckFailed => CheckFailedCause,
            SetupFailure.DiskFull => string.IsNullOrEmpty(drive) ? $"The model needs {size} of free space." : $"The model needs {size} on {drive.TrimEnd('\\')}.",
            _ => UnfinishedDownloadCause
        };
        FailureDetailText.Text = message;
        // Showing a different failure redraws the rail, so its mark lands on the step that failed.
        shownView = null;
        ShowView(SetupView.Failed);
    }

    private string FailedHeading => failure switch
    {
        SetupFailure.CheckFailed => CheckFailedHeading,
        SetupFailure.DiskFull => DiskFullHeading,
        _ => FailureHeading
    };

    // The one place that decides what each view shows: the rail, the pane, and the footer.
    private void ShowView(SetupView view)
    {
        var changed = shownView != view;
        shownView = view;
        HeadingText.Text = Heading(view);
        ShowOnly(view switch
        {
            SetupView.Choose => ChoosePane,
            SetupView.Review => ReviewPane,
            SetupView.Downloading or SetupView.Checking => ProgressPane,
            _ => StoppedPane
        }, ChoosePane, ReviewPane, ProgressPane, StoppedPane);
        ProgressAmountText.Visibility = Shown(view == SetupView.Downloading);
        ProgressBar.Visibility = Shown(view == SetupView.Downloading);
        ProgressNextText.Visibility = Shown(view == SetupView.Downloading);
        CheckingPanel.Visibility = Shown(view == SetupView.Checking);
        FailureDetailPanel.Visibility = Shown(view == SetupView.Failed);
        VisualCppLink.Visibility = Shown(view == SetupView.EngineCouldNotStart);
        (StatusText.Text, NextStepText.Text) = view switch
        {
            SetupView.EngineMissing => (MissingEngineCause, MissingEngineStep),
            SetupView.EngineCouldNotStart => (EngineCouldNotStartCause, EngineCouldNotStartStep),
            SetupView.Failed => (failureCause, failure == SetupFailure.DiskFull ? DiskFullStep : StartsOverNotice),
            _ => (string.Empty, string.Empty)
        };

        // A download can't be left for step 1, so Back is only on the review.
        BackButton.Visibility = Shown(view == SetupView.Review);
        FooterNoteText.Visibility = Shown(view == SetupView.Downloading);
        ContinueButton.Visibility = Shown(view == SetupView.Choose);
        DownloadButton.Visibility = Shown(view == SetupView.Review);
        DownloadAgainButton.Visibility = Shown(view == SetupView.Failed);
        GetZipButton.Visibility = Shown(view == SetupView.EngineMissing);
        CheckAgainButton.Visibility = Shown(view is SetupView.EngineMissing or SetupView.EngineCouldNotStart);
        // Alone in the footer, Quit's text ends where a key's face would, so the right edge holds still between steps.
        var quitIsLast = new[] { ContinueButton, DownloadButton, DownloadAgainButton, GetZipButton, CheckAgainButton }.All(button => button.Visibility != Visibility.Visible);
        QuitButton.Margin = new Thickness(0, 0, quitIsLast ? -QuitButton.Padding.Right : 0, 0);
        // Missing engine: getting the ZIP is the way forward, checking again is beside it. A stopped engine: checking again is the way forward.
        if (view == SetupView.EngineCouldNotStart)
            CheckAgainButton.Style = (Style)FindResource("PrimaryButton");
        else
            CheckAgainButton.ClearValue(StyleProperty);
        ShowDownloadReadiness();
        if (!changed)
            return;
        ShowRail(view);
        // Work under way breathes: the bar while it fills, the words while the files are checked. Nothing moves with animations off.
        if (view == SetupView.Downloading)
            CalmMotion.Breathe(ProgressBar, full: 1, low: 0.75);
        else
            CalmMotion.Still(ProgressBar, 1);
        if (view == SetupView.Checking)
            CalmMotion.Breathe(CheckingText, full: 1, low: 0.6);
        else
            CalmMotion.Still(CheckingText, 1);
    }

    private static string StepName(int number) => number switch
    {
        1 => "Choose",
        2 => "Review and download",
        _ => "Check"
    };

    private string Heading(SetupView view)
    {
        var name = ModelLibraryEditor.ShortName(ChosenModel);
        return view switch
        {
            SetupView.Choose => "Choose a speech model",
            SetupView.Review => $"Download {name}",
            SetupView.Downloading => $"Downloading {name}",
            SetupView.Checking => "Checking the download",
            SetupView.Failed => FailedHeading,
            SetupView.EngineMissing => MissingEngineHeading,
            _ => EngineCouldNotStartHeading
        };
    }

    // The journey's three steps, or, while the engine is missing or stopped, the one thing needed before setup can start.
    private void ShowRail(SetupView view)
    {
        var beforeSetup = view is SetupView.EngineMissing or SetupView.EngineCouldNotStart;
        BeforeSetupLabel.Visibility = Shown(beforeSetup);
        // The rail names the engine's state in the heading's words.
        var engineState = Heading(view);
        SetupRailStep[] steps = beforeSetup
            ? [new(0, engineState, SetupRailState.Needed, IsLast: true, $"Before setup: {engineState}")]
            : [.. Enumerable.Range(1, JourneySteps).Select(number => JourneyStep(number, view, FailedHeading, failure))];
        RailSteps.ItemsSource = steps;
        var current = steps.First(step => step.State != SetupRailState.Done);
        RailSteps.SetValue(AutomationProperties.NameProperty, current.SpokenName);
        UIElementAutomationPeer.CreatePeerForElement(RailSteps)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private static SetupRailStep JourneyStep(int number, SetupView view, string failedHeading, SetupFailure failure)
    {
        var currentNumber = view switch
        {
            SetupView.Choose => 1,
            SetupView.Checking => 3,
            SetupView.Failed when failure == SetupFailure.CheckFailed => 3,
            _ => 2
        };
        var state = number < currentNumber ? SetupRailState.Done
            : number > currentNumber ? SetupRailState.ToCome
            : view == SetupView.Failed ? SetupRailState.Stopped
            : SetupRailState.Current;
        var label = state == SetupRailState.Stopped ? failedHeading : StepName(number);
        return new(number, label, state, IsLast: number == JourneySteps, $"Step {number} of {JourneySteps}, {label}");
    }

    private static void ShowOnly(UIElement shown, params UIElement[] all)
    {
        foreach (var element in all)
            element.Visibility = Shown(element == shown);
    }

    private static Visibility Shown(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    public void CloseAfterSuccess() { completed = true; Close(); }

    private void Continue(object sender, RoutedEventArgs e)
    {
        ShowView(SetupView.Review);
        KeepKeyboardFocus(TermsCheckBox);
    }

    private void Back(object sender, RoutedEventArgs e)
    {
        ShowView(SetupView.Choose);
        KeepKeyboardFocus(ChosenRow());
    }

    // The chosen model's row: a template's only child is the radio button.
    private UIElement ChosenRow()
    {
        var chosen = ModelChoices.Items.Cast<SetupModelChoice>().First(choice => choice.IsChosen);
        var row = (ContentPresenter)ModelChoices.ItemContainerGenerator.ContainerFromItem(chosen);
        return (UIElement)System.Windows.Media.VisualTreeHelper.GetChild(row, 0);
    }

    // A step change hides the button that was pressed, so a keyboard user's focus moves to the new step's first control.
    private void KeepKeyboardFocus(UIElement target)
    {
        if (IsKeyboardFocusWithin)
            Dispatcher.BeginInvoke(() => target.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void Download(object sender, RoutedEventArgs e) => DownloadRequested?.Invoke(ChosenModel);
    // The terms for this model were accepted before the failed download, so it starts again straight away.
    private void DownloadAgain(object sender, RoutedEventArgs e) => DownloadRequested?.Invoke(ChosenModel);
    private void CheckAgain(object sender, RoutedEventArgs e) => RetryRequested?.Invoke();
    private void TermsChanged(object sender, RoutedEventArgs e) => ShowDownloadReadiness();

    // Download waits for the ticked terms, and says so under the box while it waits.
    private void ShowDownloadReadiness()
    {
        DownloadButton.IsEnabled = TermsCheckBox.IsChecked == true;
        DownloadReasonText.Visibility = Shown(!DownloadButton.IsEnabled);
    }

    private void OpenModelTerms(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(ModelTermsBrowserStartInfo(ChosenModel));
        e.Handled = true;
    }

    private void GetCompleteZip(object sender, RoutedEventArgs e) => ProjectLinks.GetCompleteZip();
    private void GetVisualCppRedistributable(object sender, RoutedEventArgs e) => SystemLinks.GetVisualCppRedistributable();
    private void Quit(object sender, RoutedEventArgs e) => CancelRequested?.Invoke();
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
    private void DragWindow(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove(); }
}

internal enum SetupView
{
    Choose,
    Review,
    Downloading,
    Checking,
    Failed,
    EngineMissing,
    EngineCouldNotStart
}

internal enum SetupFailure
{
    Unfinished,
    ConnectionDropped,
    CheckFailed,
    DiskFull
}

internal enum SetupRailState
{
    ToCome,
    Current,
    Done,
    Stopped,
    Needed
}

// One mark on the rail. It shows position only; the rail can't be clicked.
internal sealed record SetupRailStep(int Number, string Label, SetupRailState State, bool IsLast, string SpokenName);

internal sealed class SetupModelChoice(SpeechModelDefinition model, bool isChosen, Action<SpeechModelDefinition> chosen) : INotifyPropertyChanged
{
    private bool isChosen = isChosen;

    public event PropertyChangedEventHandler? PropertyChanged;

    public SpeechModelDefinition Model { get; } = model;
    // The name Settings uses for the same model.
    public string ShortName => ModelLibraryEditor.ShortName(Model);
    public SpeechModelFacts Facts { get; } = SpeechModelFacts.For(model);
    public bool IsRecommended => Model.Id == SpeechModelCatalog.DefaultId;
    public string Size => SizeOf(Model);

    // The download size as a table value: "681 MiB".
    internal static string SizeOf(SpeechModelDefinition model) => $"{model.Manifest.ExpectedBytes / 1024d / 1024d:F0} MiB";

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
