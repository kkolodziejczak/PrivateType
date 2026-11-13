using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media.Animation;
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
    internal const string SharedStorageNotice = "The model files stay on this computer and are shared by PrivateType versions on this Windows account.";
    internal const string PortableStorageNotice = "The model files stay in this copy's app\\models folder and aren't shared with other PrivateType versions.";
    private const int JourneySteps = 3;
    private SpeechModelDefinition chosenModel;
    private SetupView? shownView;
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
        ProgressNextText.Text = checking
            ? $"{Mebibytes(total)} MiB downloaded. Checking the files…"
            : "Next, the files are checked. Then dictation is ready.";
        // Checking has no measurable progress, so its bar is whole and pulses: work under way, not a share done.
        ProgressBar.Value = checking ? 100 : total == 0 ? 0 : Math.Min(100, downloaded * 100d / total);
        ProgressBar.SetValue(AutomationProperties.NameProperty, checking ? "Checking the files" : ProgressAmountText.Text);
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

    public void ShowFailure(Exception exception) => ShowFailure(exception.Message, IsDroppedConnection(exception));

    // The plain cause and what Download again does lead; the system's message follows as a quieter, selectable detail.
    internal void ShowFailure(string message, bool connectionDropped)
    {
        failureCause = connectionDropped ? DroppedConnectionCause : UnfinishedDownloadCause;
        FailureDetailText.Text = message;
        ShowView(SetupView.Failed);
    }

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
        FailureDetailPanel.Visibility = Shown(view == SetupView.Failed);
        (StatusText.Text, NextStepText.Text) = view switch
        {
            SetupView.EngineMissing => (MissingEngineCause, MissingEngineStep),
            SetupView.EngineCouldNotStart => (EngineCouldNotStartCause, EngineCouldNotStartStep),
            SetupView.Failed => (failureCause, StartsOverNotice),
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
        Pulse(ProgressBar, view is SetupView.Downloading or SetupView.Checking);
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
            SetupView.Failed => FailureHeading,
            SetupView.EngineMissing => MissingEngineHeading,
            _ => EngineCouldNotStartHeading
        };
    }

    // The journey's three steps, or, while the engine is missing or stopped, the one thing needed before setup can start.
    private void ShowRail(SetupView view)
    {
        var beforeSetup = view is SetupView.EngineMissing or SetupView.EngineCouldNotStart;
        BeforeSetupLabel.Visibility = Shown(beforeSetup);
        SetupRailStep[] steps = beforeSetup
            ? [new(0, "Speech engine needed", SetupRailState.Needed, IsLast: true, "Before setup: speech engine needed")]
            : [.. Enumerable.Range(1, JourneySteps).Select(number => JourneyStep(number, view))];
        RailSteps.ItemsSource = steps;
        var current = steps.First(step => step.State != SetupRailState.Done);
        RailSteps.SetValue(AutomationProperties.NameProperty, current.SpokenName);
        UIElementAutomationPeer.CreatePeerForElement(RailSteps)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private static SetupRailStep JourneyStep(int number, SetupView view)
    {
        var currentNumber = view switch
        {
            SetupView.Choose => 1,
            SetupView.Checking => 3,
            _ => 2
        };
        var state = number < currentNumber ? SetupRailState.Done
            : number > currentNumber ? SetupRailState.ToCome
            : view == SetupView.Failed ? SetupRailState.Stopped
            : SetupRailState.Current;
        var label = state == SetupRailState.Stopped ? FailureHeading : StepName(number);
        return new(number, label, state, IsLast: number == JourneySteps, $"Step {number} of {JourneySteps}, {label}");
    }

    private static void ShowOnly(UIElement shown, params UIElement[] all)
    {
        foreach (var element in all)
            element.Visibility = Shown(element == shown);
    }

    private static Visibility Shown(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    // Work under way pulses, so a wait of several seconds still shows that something is happening.
    private static void Pulse(UIElement step, bool underWay)
        => step.BeginAnimation(OpacityProperty, underWay
            ? new DoubleAnimation(1, 0.75, TimeSpan.FromSeconds(0.8)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever }
            : null);

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
