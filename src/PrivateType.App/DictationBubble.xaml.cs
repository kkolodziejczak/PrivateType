using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using PrivateType.Core;
using Forms = System.Windows.Forms;

namespace PrivateType.App;

// The one key a failure in the bubble offers beside its message.
public enum BubbleAction
{
    MicrophoneSettings,
    Diagnostics
}

public partial class DictationBubble : Window
{
    internal const bool RecordingWaveformBlinks = false;
    internal const int SpectrumBarCount = 44;
    // The live level is drawn as a few bars, each the loudest of its group of spectrum bins.
    internal const int MeterBarCount = 5;
    internal const string ModelLoadingTitle = "Loading the model";
    internal const string ModelLoadingHint = "Keep holding. Listening starts when it's ready.";
    internal const string ModelLoadingToggleHint = "Listening starts when it's ready.";
    internal const string ModelStillLoadingHint = "Still loading. This can take up to a minute the first time.";
    internal const string ListeningTitle = "Listening";
    internal const string FinalizingTitle = "Transcribing";
    internal const string FinalizingHint = "Your text goes in when it's ready.";
    internal const string NotInsertedTitle = "Not inserted";
    internal const string NotSavedDetail = "not saved to Recent dictations";
    internal const string CopiedTitle = "Copied";
    internal const string CopiedDetail = "press Ctrl+V to paste it";
    internal const string CopyFailedTitle = "Couldn't copy";
    internal const string CopyFailedDetail = "the clipboard is busy, try again";
    internal static readonly TimeSpan StillLoadingAfter = TimeSpan.FromSeconds(8);

    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WmNchitTest = 0x0084;
    private const int WmExitSizeMove = 0x0232;
    private const double ReadyWidth = 64;
    internal const double ActiveWidth = 360;
    private const double WorkAreaBottomClearance = 8;
    private const double MinimumVisibleExtent = 24;
    private const nint HtClient = 1;
    private const nint HtTransparent = -1;
    private readonly AdaptiveAudioMeter adaptiveAudioMeter = new();
    private readonly List<ScaleTransform> meterScales = [];
    private readonly double[] smoothedSpectrum = new double[SpectrumBarCount];
    private readonly DispatcherTimer stillLoadingTimer = new() { Interval = StillLoadingAfter };
    private bool active;
    private bool recordingIndicatorVisible;
    private bool recordingVisualsActive;
    private bool dragActive;
    private bool noticeShown;
    private IReadOnlyList<ShortcutBinding> bindings = ShortcutBinding.Defaults;
    // A dictation that wasn't inserted, held in memory only while its notice is up; never written anywhere.
    private string? heldText;
    private double bindingsCardShift;
    private bool restingModelLoaded;
    private PointInt dragGrabOffset;
    private readonly NativeMethods.MouseHookProc mouseHookProc;
    private nint mouseHook;

    public DictationBubble()
    {
        InitializeComponent();
        mouseHookProc = OnMouseHook;
        Menu.PlacementTarget = BubbleShell;
        Menu.Placement = PlacementMode.Right;
        Menu.HorizontalOffset = 8;
        CreateWaveformBars();
        stillLoadingTimer.Tick += (_, _) =>
        {
            stillLoadingTimer.Stop();
            Hint.Text = ModelStillLoadingHint;
        };
        MicrophoneAction.Content = SystemLinks.MicrophoneSettingsAction;
    }

    private System.Windows.Controls.ContextMenu Menu => BubbleShell.ContextMenu;

    // The bubble never activates, so WPF never sees the outside click or the app
    // deactivation that would normally dismiss its menu. Close it explicitly.
    public void CloseMenu()
    {
        if (Menu.IsOpen)
            Menu.IsOpen = false;
    }

    // Re-shows, un-minimizes, re-asserts topmost and brings the bubble back onto a
    // live display after sleep, display or session changes.
    public void RecoverPlacement(PortableSettings settings)
    {
        CloseMenu();
        if (!IsVisible)
            Show();
        if (WindowState != WindowState.Normal)
            WindowState = WindowState.Normal;
        RaiseAboveOtherWindows();
        if (IsOffEveryScreen())
            ApplyPosition(settings);
        else
            ClampToWorkArea(CurrentWorkArea());
    }

    public event Action<string, double, double>? PositionChanged;
    public event Action? SettingsRequested;
    public event Action? VocabularyRequested;
    public event Action? TeachRequested;
    public event Action? HistoryRequested;
    public event Action? DiagnosticsRequested;
    public event Action? MicrophoneSettingsRequested;
    // Copy was pressed on a not-inserted notice; the handler copies the text and reports back through ShowCopyResult.
    public event Action<string>? CopyRequested;
    // Raised when a not-inserted dictation starts or stops being held, so the tray can offer the same Copy.
    public event Action? HeldTextChanged;

    internal string? HeldText => heldText;

    // The menu shows only whether a result exists, never its text.
    public void SetTeachAvailable(bool available) => TeachMenuItem.IsEnabled = available;

    internal bool IsTeachAvailable => TeachMenuItem.IsEnabled;
    public event Action? QuitRequested;
    public event Action<bool>? RecordingIndicatorChanged;

    public void ShowReady(PortableSettings settings, bool modelLoaded)
    {
        if (!IsVisible)
        {
            Show();
            ApplyPosition(settings);
        }
        else if (IsOffEveryScreen())
        {
            ApplyPosition(settings);
        }

        LeaveState(active: false);
        BubbleShell.Opacity = OpacityForReadyState(modelLoaded);
        ApplyReadyVisuals(modelLoaded);
        AnnounceState(modelLoaded ? "Ready" : "Model not loaded");
        SetWidthAroundCenter(ReadyWidth);
        bindings = settings.Shortcuts;
        RestLayer.Visibility = Visibility.Visible;
        Card.Visibility = Visibility.Collapsed;
    }

    // Shows the bubble where the text will go: the monitor of the window being dictated into.
    // The pointer's monitor is the fallback when no such window is active.
    public void MoveToDictationScreen()
    {
        MoveToScreen(DictationScreen());
        RaiseAboveOtherWindows();
    }

    // Windows can leave a topmost window behind ordinary ones, so re-enter the topmost band
    // instead of trusting the Topmost flag.
    private void RaiseAboveOtherWindows()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != nint.Zero)
            NativeMethods.SetWindowPos(handle, NativeMethods.HwndTopmost, 0, 0, 0, 0, NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
    }

    internal void MoveToScreen(Forms.Screen targetScreen)
    {
        var sourceScreen = CurrentScreen();
        var sourceWorkArea = WorkAreaFor(sourceScreen);
        var targetWorkArea = WorkAreaFor(targetScreen);
        var bubbleWidth = ActualWidth > 0 ? ActualWidth : Width;
        var bubbleHeight = ActualHeight > 0 ? ActualHeight : Height;
        Left = MapCoordinateToWorkArea(Left, bubbleWidth, sourceWorkArea.Left, sourceWorkArea.Width, targetWorkArea.Left, targetWorkArea.Width);
        Top = MapCoordinateToWorkArea(Top, bubbleHeight, sourceWorkArea.Top, sourceWorkArea.Height, targetWorkArea.Top, targetWorkArea.Height);
    }

    private static Forms.Screen DictationScreen()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(foreground, out var processId);
        var className = new System.Text.StringBuilder(64);
        NativeMethods.GetClassNameW(foreground, className, className.Capacity);
        return FollowsForegroundWindow(foreground, processId, (uint)Environment.ProcessId, className.ToString())
            ? Forms.Screen.FromHandle(foreground)
            : Forms.Screen.FromPoint(Forms.Cursor.Position);
    }

    // The desktop spans every monitor and PrivateType's own windows are not dictation targets.
    internal static bool FollowsForegroundWindow(nint foreground, uint processId, uint ownProcessId, string className) =>
        foreground != nint.Zero && processId != 0 && processId != ownProcessId &&
        className is not ("Progman" or "WorkerW");

    public void ShowRecording(string localeCode)
    {
        if (recordingVisualsActive)
            return;

        var selectedWorkArea = CurrentWorkArea();
        LeaveState(active: true);
        recordingVisualsActive = true;
        ShowCard(CardMark.Listening, ListeningTitle, LanguageName(localeCode));
        AnnounceState(ListeningTitle);
        ResetAudioVisuals();
        SetWidthAroundCenter(ActiveWidth, selectedWorkArea);
        StartRecordingIndicator();
        WaveformBars.Visibility = Visibility.Visible;
        Transcript.Opacity = 1;
        SetTranscript(string.Empty);
        TranscriptViewport.Visibility = Visibility.Visible;
        ClampToWorkAreaAfterLayout(selectedWorkArea);
    }

    public void ShowModelLoading(bool toggleMode = false)
    {
        LeaveState(active: true);
        ShowCard(CardMark.Working, ModelLoadingTitle, detail: null);
        AnnounceState(ModelLoadingTitle);
        SetWidthAroundCenter(ActiveWidth);
        ShowHint(toggleMode ? ModelLoadingToggleHint : ModelLoadingHint);
        stillLoadingTimer.Start();
        ClampToWorkAreaAfterLayout();
    }

    public void ShowTranscript(string transcript)
    {
        SetTranscript(transcript);
        ClampToWorkAreaAfterLayout();
    }

    public void ShowAudioMeter(AudioMeter meter)
    {
        if (!recordingVisualsActive)
            return;

        // The gain keeps the spectrum's shape readable; the bars' height still follows the real loudness.
        var loudness = MeterLoudness(meter.Level);
        meter = adaptiveAudioMeter.Normalize(meter);
        var level = Math.Clamp(meter.Level, 0, 1);
        for (var index = 0; index < smoothedSpectrum.Length; index++)
        {
            var target = index < meter.Spectrum.Count ? Math.Clamp(meter.Spectrum[index], 0, 1) * loudness : 0;
            smoothedSpectrum[index] += (target - smoothedSpectrum[index]) * 0.68;
        }

        for (var bar = 0; bar < meterScales.Count; bar++)
        {
            var start = bar * SpectrumBarCount / MeterBarCount;
            var end = (bar + 1) * SpectrumBarCount / MeterBarCount;
            var loudest = 0.0;
            for (var bin = start; bin < end; bin++)
                loudest = Math.Max(loudest, smoothedSpectrum[bin]);
            meterScales[bar].ScaleY = Math.Max(0.15, loudest);
        }

        WaveformBars.Opacity = 0.7 + (level * 0.3);
    }

    // Maps the raw level to a bar ceiling: about -32 dB keeps a quarter of the height, -6 dB and louder the full height.
    internal static double MeterLoudness(double rawLevel)
    {
        var decibels = 20 * Math.Log10(Math.Max(rawLevel, 0.0001));
        return Math.Clamp(0.25 + (0.75 * (decibels + 32) / 26), 0.25, 1);
    }

    // After release the microphone is off but text has not arrived yet; say so, keeping any preview, dimmed.
    // It can arrive without a rendered recording state, so set up the card itself.
    public void ShowFinalizing()
    {
        var hasWords = !string.IsNullOrEmpty(Transcript.Text) && TranscriptViewport.Visibility == Visibility.Visible;
        LeaveState(active: true);
        ShowCard(CardMark.Working, FinalizingTitle, detail: null);
        AnnounceState(FinalizingTitle);
        SetWidthAroundCenter(ActiveWidth);
        if (hasWords)
        {
            Transcript.Opacity = 0.7;
            TranscriptViewport.Visibility = Visibility.Visible;
        }
        else
        {
            ShowHint(FinalizingHint);
        }

        ClampToWorkAreaAfterLayout();
    }

    // A dictation that wasn't inserted: the words themselves, with Copy, and Recent dictations when the list kept them.
    // The text is held only for this notice's life; nothing is copied until Copy is pressed.
    public void ShowNotInserted(TargetEligibility cause, string text, bool savedToHistory)
    {
        LeaveState(active: false);
        noticeShown = true;
        heldText = text;
        // The line holds one detail: why, or, when nothing else keeps the words, that they aren't saved.
        var detail = NotInsertedCause(cause);
        ShowCard(CardMark.Warning, NotInsertedTitle, savedToHistory ? detail : NotSavedDetail);
        AnnounceState(NotInsertedTitle);
        SetWidthAroundCenter(ActiveWidth);
        HeldWords.Text = CutToLines(text, HeldLines);
        System.Windows.Automation.AutomationProperties.SetName(HeldWords, text);
        HeldWords.Visibility = Visibility.Visible;
        ShowActions(savedToHistory ? [HistoryAction, CopyAction] : [CopyAction]);
        var kept = savedToHistory ? "It is in Recent dictations." : "It wasn't kept, because Recent dictations is off.";
        AnnounceMessage($"{NotInsertedTitle}: {detail}. {kept}");
        HeldTextChanged?.Invoke();
        ClampToWorkAreaAfterLayout();
    }

    // What Copy did. The words stay either way, so a failed copy can be tried again.
    public void ShowCopyResult(bool copied)
    {
        if (heldText is null)
            return;

        SetStateLine(copied ? CardMark.Success : CardMark.Failure, copied ? CopiedTitle : CopyFailedTitle, copied ? CopiedDetail : CopyFailedDetail);
        AnnounceState(copied ? CopiedTitle : CopyFailedTitle);
        AnnounceMessage(copied ? $"{CopiedTitle}. Press Ctrl+V to paste it." : $"{CopyFailedTitle}: the clipboard is busy. Try again.");
    }

    // A short notice about a recent dictation handed back: copied instead of pasted, or neither.
    public void ShowNotice(bool succeeded, string title, string message)
    {
        LeaveState(active: false);
        noticeShown = true;
        ShowCard(succeeded ? CardMark.Success : CardMark.Failure, title, detail: null);
        AnnounceState(title);
        SetWidthAroundCenter(ActiveWidth);
        ShowHint(message);
        AnnounceMessage($"{title}. {message}");
        ClampToWorkAreaAfterLayout();
    }

    // The title names what failed; one sentence says what to do, and one key takes the first step of it.
    public void ShowError(string title, string message, BubbleAction action)
    {
        LeaveState(active: true);
        noticeShown = true;
        ShowCard(CardMark.Failure, title, detail: null);
        AnnounceState(title);
        SetWidthAroundCenter(ActiveWidth);
        ShowHint(message);
        ShowActions([action == BubbleAction.MicrophoneSettings ? MicrophoneAction : DiagnosticsAction]);
        AnnounceMessage($"{title}. {message}");
        ClampToWorkAreaAfterLayout();
    }

    private const int HeldLines = 3;

    // The most whole words that, with an ellipsis, wrap to the given lines across the card. WPF's own trimming under a
    // height limit drops the last line instead of ending it with the ellipsis.
    private string CutToLines(string text, int lines)
    {
        // The card's inner width: its width less the 1 px border and 12 px padding on each side.
        var width = ActiveWidth - 2 - 24;
        var typeface = new Typeface(HeldWords.FontFamily, HeldWords.FontStyle, HeldWords.FontWeight, HeldWords.FontStretch);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        bool Fits(string candidate) =>
            new FormattedText(candidate, System.Globalization.CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight, typeface,
                HeldWords.FontSize, System.Windows.Media.Brushes.Black, pixelsPerDip) { MaxTextWidth = width, LineHeight = HeldWords.LineHeight }
            .Height <= (lines * HeldWords.LineHeight) + 0.5;

        if (Fits(text))
            return text;
        var words = text.Split(' ');
        var (low, high) = (0, words.Length);
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (Fits(string.Join(' ', words[..middle]) + "…"))
                low = middle;
            else
                high = middle - 1;
        }

        return string.Join(' ', words[..low]) + "…";
    }

    internal static string NotInsertedCause(TargetEligibility cause) => cause switch
    {
        TargetEligibility.Changed => "the window changed",
        TargetEligibility.Ineligible => "the window doesn't accept typing",
        _ => "the window closed"
    };

    // The language as people say it: "English", not "English (United States)" or "EN".
    internal static string LanguageName(string localeCode)
    {
        var name = RecognitionLocaleCatalog.Get(localeCode).DisplayName;
        var bracket = name.IndexOf(" (", StringComparison.Ordinal);
        return bracket < 0 ? name : name[..bracket];
    }

    // Every state starts from the same empty card: no words, no hint, no keys, no motion, no held text.
    private void LeaveState(bool active)
    {
        CloseMenu();
        HideBindingsCard();
        this.active = active;
        recordingVisualsActive = false;
        noticeShown = false;
        stillLoadingTimer.Stop();
        StopRecordingIndicator();
        BubbleShell.Opacity = 1;
        Hint.Visibility = Visibility.Collapsed;
        WaveformBars.Visibility = Visibility.Collapsed;
        TranscriptViewport.Visibility = Visibility.Collapsed;
        HeldWords.Visibility = Visibility.Collapsed;
        HeldWords.Text = string.Empty;
        ShowActions([]);
        if (heldText is not null)
        {
            heldText = null;
            HeldTextChanged?.Invoke();
        }
    }

    private void ShowCard(CardMark mark, string title, string? detail)
    {
        RestLayer.Visibility = Visibility.Collapsed;
        Card.Visibility = Visibility.Visible;
        SetStateLine(mark, title, detail);
    }

    // The slim line over the words: a mark, the state word, then quieter detail.
    private void SetStateLine(CardMark mark, string title, string? detail)
    {
        StateWord.Text = title;
        StateDetail.Text = string.IsNullOrEmpty(detail) ? string.Empty : $"  ·  {detail}";
        StateDot.Visibility = mark == CardMark.Listening ? Visibility.Visible : Visibility.Collapsed;
        StateRing.Visibility = mark == CardMark.Working ? Visibility.Visible : Visibility.Collapsed;
        WarningGlyph.Visibility = mark is CardMark.Warning or CardMark.Failure ? Visibility.Visible : Visibility.Collapsed;
        WarningGlyph.Foreground = Brush(mark == CardMark.Failure ? "ColorDanger" : "ColorWarning");
        SuccessGlyph.Visibility = mark == CardMark.Success ? Visibility.Visible : Visibility.Collapsed;
        // Work under way breathes; with animations off the ring and the state word say it, still.
        if (mark == CardMark.Working)
            CalmMotion.Breathe(StateRing, full: 1, low: 0.25);
        else
            CalmMotion.Still(StateRing, 1);
    }

    private void ShowHint(string text)
    {
        Hint.Text = text;
        Hint.Visibility = Visibility.Visible;
    }

    private void ShowActions(System.Windows.Controls.Button[] shown)
    {
        foreach (var key in new[] { HistoryAction, CopyAction, MicrophoneAction, DiagnosticsAction })
            key.Visibility = shown.Contains(key) ? Visibility.Visible : Visibility.Collapsed;
        ActionRow.Visibility = shown.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // Hovering the resting key lists the shortcuts as keys, one line per language: under the key, or over it where
    // there is no room below. The key itself stays where it is.
    private void EnterRestingKey(object sender, System.Windows.Input.MouseEventArgs e) => ShowBindingsCard();

    internal void ShowBindingsCard()
    {
        if (active || noticeShown || dragActive || Card.Visibility == Visibility.Visible || BindingsCard.Visibility == Visibility.Visible)
            return;

        BindingsList.Children.Clear();
        foreach (var binding in HotkeyCatalog.FromBindings(bindings))
            BindingsList.Children.Add(BindingRow(binding.Label, LanguageName(binding.LocaleCode)));
        BindingsCard.Visibility = Visibility.Visible;
        BindingsCard.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        var cardHeight = BindingsCard.DesiredSize.Height;
        var above = Top + ActualHeight + cardHeight > CurrentWorkArea().Bottom - WorkAreaBottomClearance;
        RestLayer.Children.Remove(BindingsCard);
        RestLayer.Children.Insert(above ? 0 : 1, BindingsCard);
        bindingsCardShift = above ? cardHeight : 0;
        BubbleShell.Opacity = 1;
        SetWidthAroundCenter(Math.Max(ReadyWidth, Math.Ceiling(BindingsCard.DesiredSize.Width)));
        Top -= bindingsCardShift;
        UpdateLayout();
    }

    private void LeaveRestingKey(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (BindingsCard.Visibility != Visibility.Visible)
            return;
        HideBindingsCard();
        BubbleShell.Opacity = OpacityForReadyState(restingModelLoaded);
        SetWidthAroundCenter(ReadyWidth);
    }

    private void HideBindingsCard()
    {
        if (BindingsCard.Visibility != Visibility.Visible)
            return;
        BindingsCard.Visibility = Visibility.Collapsed;
        Top += bindingsCardShift;
        bindingsCardShift = 0;
    }

    // One shortcut: its keys, then the language it dictates in.
    private static StackPanel BindingRow(string label, string language)
    {
        var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        foreach (var key in KeycapLegendsConverter.Split(label))
            row.Children.Add(Keycap(key.Legend, first: row.Children.Count == 0));
        row.Children.Add(new TextBlock
        {
            Text = language,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            FontFamily = (System.Windows.Media.FontFamily)System.Windows.Application.Current.Resources["AppFont"],
            FontSize = 12,
            Foreground = Brush("ColorNeutral400")
        });
        System.Windows.Automation.AutomationProperties.SetName(row, $"{label}, {language}");
        return row;
    }

    private static Border Keycap(string legend, bool first) => new()
    {
        MinWidth = 22,
        Height = 20,
        Margin = new Thickness(first ? 0 : 3, 0, 0, 0),
        Padding = new Thickness(5, 0, 5, 0),
        CornerRadius = new CornerRadius(4),
        Background = Brush("ColorKeycap"),
        BorderBrush = Brush("ColorKeycapEdge"),
        BorderThickness = new Thickness(0, 0, 0, 2),
        Child = new TextBlock
        {
            Text = legend,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            FontFamily = (System.Windows.Media.FontFamily)System.Windows.Application.Current.Resources["MonoFont"],
            FontSize = 11,
            Foreground = Brush("ColorText")
        }
    };

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = (HwndSource)PresentationSource.FromVisual(this)!;
        source.AddHook(WindowMessageHook);
        var handle = new WindowInteropHelper(this).Handle;
        var extendedStyle = NativeMethods.GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        NativeMethods.SetWindowLongPtr(handle, GwlExStyle, (nint)(extendedStyle | WsExNoActivate));

        // The click-through + non-activatable + transparent window does not reliably
        // route drag mouse events, so track the drag from a global low-level mouse hook
        // instead. This callback runs on this (UI) thread.
        mouseHook = NativeMethods.SetWindowsHookExW(
            NativeMethods.WhMouseLl,
            mouseHookProc,
            NativeMethods.GetModuleHandleW(null),
            0);
        if (mouseHook == nint.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install the bubble drag hook.");
    }

    private nint WindowMessageHook(nint handle, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmNchitTest)
        {
            // While dragging, report the whole window as a client-area hit so it
            // does not become click-through after the pointer leaves the icon.
            // We move the window in OnMouseHook, so we must
            // NOT return a caption hit (that would let Windows drag the window
            // natively and fight our manual move).
            if (dragActive)
            {
                handled = true;
                return HtClient;
            }

            var cursor = CursorScreenPosition(lParam);
            if (IsScreenPointOverIcon(cursor) || IsScreenPointOver(ActionRow, cursor))
            {
                // The drag handle and the card's keys stay interactive.
                handled = true;
                return HtClient;
            }

            // The rest of the bubble is click-through so you can see what is
            // behind it while the module is loading or speaking.
            handled = true;
            return HtTransparent;
        }

        if (message == WmExitSizeMove)
            ReportPosition();

        return nint.Zero;
    }

    internal static PointInt CursorScreenPosition(nint lParam)
    {
        var packed = lParam.ToInt64();
        return new PointInt(
            unchecked((short)(packed & 0xFFFF)),
            unchecked((short)((packed >> 16) & 0xFFFF)));
    }

    // The drag handle: the key at rest, the card's state line while a card is up. While dictating the card can't be
    // dragged, so its line stays click-through like the rest of it.
    private bool IsScreenPointOverIcon(PointInt cursor) =>
        Card.Visibility == Visibility.Visible ? !active && IsScreenPointOver(CardHeader, cursor) : IsScreenPointOver(IconTile, cursor);

    private static bool IsScreenPointOver(FrameworkElement element, PointInt cursor)
    {
        if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            return false;

        var local = element.PointFromScreen(new System.Windows.Point(cursor.X, cursor.Y));
        return local.X >= 0 && local.X < element.ActualWidth
            && local.Y >= 0 && local.Y < element.ActualHeight;
    }

    // A notice stays up while the pointer rests on it.
    internal bool IsNoticeUnderPointer()
    {
        if (!noticeShown || !NativeMethods.GetCursorPos(out var cursor))
            return false;
        return IsScreenPointOver(Card, new PointInt(cursor.X, cursor.Y));
    }

    private void CreateWaveformBars()
    {
        for (var index = 0; index < MeterBarCount; index++)
        {
            var scale = new ScaleTransform(1, 0.15);
            var bar = new System.Windows.Shapes.Rectangle
            {
                Width = 3,
                Height = 20,
                Margin = new Thickness(1.5, 0, 1.5, 0),
                RadiusX = 1.5,
                RadiusY = 1.5,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Fill = Brush("ColorAccent400"),
                RenderTransform = scale,
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5)
            };
            meterScales.Add(scale);
            WaveformBars.Children.Add(bar);
        }
    }

    // At rest only the key is a resting key; the card's keys are a pointer's only way to act on a notice.
    private void ApplyReadyVisuals(bool modelLoaded)
    {
        restingModelLoaded = modelLoaded;
        // Without a model the key is only an outline: nothing is loaded behind it yet. It is drawn at full opacity, and
        // ColorNeutral400 keeps 3:1 against the capture desktop (#5A636E) and a near-black backdrop alike.
        var fill = modelLoaded ? "ColorKeycap" : null;
        var glyph = modelLoaded ? "ColorText" : "ColorNeutral400";
        IconTile.Background = fill is null ? System.Windows.Media.Brushes.Transparent : Brush(fill);
        IconTile.BorderBrush = fill is null ? Brush(glyph) : Brush("ColorKeycapEdge");
        // 1.6 is two whole pixels at 125 %.
        IconTile.BorderThickness = fill is null ? new Thickness(1.6) : new Thickness(0, 0, 0, 3);
        IconOutline.Fill = Brush(glyph);
        IconStand.Stroke = Brush(glyph);
    }

    private void ResetAudioVisuals()
    {
        adaptiveAudioMeter.Reset();
        Array.Fill(smoothedSpectrum, 0.08);
        foreach (var scale in meterScales)
            scale.ScaleY = 0.15;
    }

    // The bubble's state word is its item status, so a screen reader can ask what the bubble is doing.
    private void AnnounceState(string state)
    {
        System.Windows.Automation.AutomationProperties.SetItemStatus(this, state);
        System.Windows.Automation.AutomationProperties.SetItemStatus(BubbleShell, state);
    }

    // A notice or a failure is read out once, politely, in full, without moving focus.
    private void AnnounceMessage(string message)
    {
        System.Windows.Automation.AutomationProperties.SetName(Card, message);
        System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(Card)?
            .RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }

    private void SetTranscript(string text)
    {
        Transcript.Text = text;
        TranscriptViewport.UpdateLayout();
        TranscriptViewport.ScrollToEnd();
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(TranscriptViewport.ScrollToEnd));
    }

    private static System.Windows.Media.Brush Brush(string resourceKey) => (System.Windows.Media.Brush)System.Windows.Application.Current.Resources[resourceKey];

    private void ApplyPosition(PortableSettings settings)
    {
        var workArea = WorkAreaFor(FindScreen(settings.PanelDisplayDeviceName));
        var left = settings.PanelLeftFraction is double x ? workArea.Left + (workArea.Width - Width) * x : workArea.Left + (workArea.Width - Width) / 2;
        var top = settings.PanelTopFraction is double y ? workArea.Top + (workArea.Height - Height) * y : workArea.Bottom - Height - 32;
        Left = Math.Clamp(left, workArea.Left, workArea.Right - Width);
        Top = Math.Clamp(top, workArea.Top, workArea.Bottom - Height);
    }

    private void ReportPosition()
    {
        var screen = CurrentScreen();
        var workArea = WorkAreaFor(screen);
        PositionChanged?.Invoke(
            screen.DeviceName,
            workArea.Width == Width ? 0 : Math.Clamp((Left - workArea.Left) / (workArea.Width - Width), 0, 1),
            workArea.Height == Height ? 0 : Math.Clamp((Top - workArea.Top) / (workArea.Height - Height), 0, 1));
    }

    protected override void OnClosed(EventArgs e)
    {
        if (mouseHook != nint.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(mouseHook);
            mouseHook = nint.Zero;
        }

        base.OnClosed(e);
    }

    internal nint OnMouseHook(int code, nint wParam, nint lParam)
    {
        if (code < 0)
            return NativeMethods.CallNextHookEx(mouseHook, code, wParam, lParam);

        if (Menu.IsOpen && !dragActive)
        {
            if (DismissesMenu(true, wParam, IsCursorOverMenu(lParam)))
                CloseMenu();
            // Never start a drag from a click that closes or targets the menu.
            return NativeMethods.CallNextHookEx(mouseHook, code, wParam, lParam);
        }

        switch (ClassifyDragHookAction(dragActive, wParam))
        {
            case DragHookAction.Start when active || !IsCursorOverIcon(lParam):
                return NativeMethods.CallNextHookEx(mouseHook, code, wParam, lParam);
            case DragHookAction.Start:
                StartDrag(lParam);
                return 1;
            case DragHookAction.Recover:
                // The button-up never reached the hook; a new press ends the stale drag
                // and passes through untouched.
                dragActive = false;
                ReportPosition();
                break;
            case DragHookAction.Move:
                MoveDrag(lParam);
                break;
            case DragHookAction.End:
                dragActive = false;
                ReportPosition();
                // We suppressed the matching button-down, so suppress the up too.
                return 1;
        }

        return NativeMethods.CallNextHookEx(mouseHook, code, wParam, lParam);
    }

    internal static bool DismissesMenu(bool menuOpen, nint message, bool overMenu) =>
        menuOpen && !overMenu && (message == NativeMethods.WmLButtonDown || message == NativeMethods.WmRButtonDown);

    private bool IsCursorOverMenu(nint lParam)
    {
        var menu = Menu;
        if (!menu.IsOpen || PresentationSource.FromVisual(menu) is not HwndSource popup || menu.ActualWidth <= 0 || menu.ActualHeight <= 0)
            return false;

        // The menu is its own window and can sit on another monitor than the bubble.
        var cursor = CursorInWindowSpace(lParam, popup.Handle);
        var local = menu.PointFromScreen(new System.Windows.Point(cursor.X, cursor.Y));
        return local.X >= 0 && local.X < menu.ActualWidth && local.Y >= 0 && local.Y < menu.ActualHeight;
    }

    internal static DragHookAction ClassifyDragHookAction(bool dragActive, nint message) =>
        (dragActive, message) switch
        {
            (false, NativeMethods.WmLButtonDown) => DragHookAction.Start,
            (true, NativeMethods.WmMouseMove) => DragHookAction.Move,
            (true, NativeMethods.WmLButtonUp) => DragHookAction.End,
            (true, NativeMethods.WmLButtonDown) => DragHookAction.Recover,
            _ => DragHookAction.None
        };

    private void StartDrag(nint lParam)
    {
        HideBindingsCard();
        var handle = new WindowInteropHelper(this).Handle;
        var cursor = CursorInWindowSpace(lParam, handle);
        NativeMethods.GetWindowRect(handle, out var window);
        dragGrabOffset = new PointInt(cursor.X - window.Left, cursor.Y - window.Top);
        dragActive = true;
    }

    // This app's coordinates do not line up across monitors scaled differently, and Windows alone
    // decides which monitor scales the bubble, sometimes by its previous one and sometimes reporting
    // another. So a placement is judged by where the bubble landed, starting with its reported monitor.
    private void MoveDrag(nint lParam)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var cursor = CursorFromHook(lParam);
        NativeMethods.GetWindowRect(handle, out var start);
        var physicalStart = PhysicalBounds(handle);
        if (PlaceGrabbedSpotUnder(handle, cursor, MonitorSpaceOf(NativeMethods.MonitorFromWindow(handle, NativeMethods.MonitorDefaultToNearest))))
            return;

        var monitors = MeasureMonitorSpaces();
        if (monitors.Any(monitor => PlaceGrabbedSpotUnder(handle, cursor, monitor)))
            return;

        // With an off-center grab, a few pixels at the border have no answer: the bubble belongs on
        // each monitor at the other monitor's size. A failed try can leave it anywhere, even off every
        // screen, so wait where it was until the cursor is past them.
        if (IsInside(physicalStart, cursor))
        {
            MoveWindowOrigin(handle, start.Left, start.Top);
            return;
        }

        // A fast cursor can land there from outside the bubble. Centered on the cursor, the bubble
        // belongs to the cursor's monitor at either size, so that placement has an answer.
        var center = new PointInt((start.Right - start.Left) / 2, (start.Bottom - start.Top) / 2);
        foreach (var monitor in monitors.OrderByDescending(monitor => IsInside(monitor.Physical, cursor)))
        {
            var mapped = MapToWindowSpace(cursor, monitor);
            MoveWindowOrigin(handle, mapped.X - center.X, mapped.Y - center.Y);
            if (IsInside(PhysicalBounds(handle), cursor))
                return;
        }
    }

    private static bool IsInside(NativeMethods.NativeRect bounds, PointInt point) =>
        point.X >= bounds.Left && point.X < bounds.Right && point.Y >= bounds.Top && point.Y < bounds.Bottom;

    private bool PlaceGrabbedSpotUnder(nint handle, PointInt cursor, MonitorSpace? monitor)
    {
        var mapped = MapToWindowSpace(cursor, monitor);
        MoveWindowOrigin(handle, mapped.X - dragGrabOffset.X, mapped.Y - dragGrabOffset.Y);
        NativeMethods.GetWindowRect(handle, out var windowSpace);
        return HoldsGrabbedSpot(cursor, dragGrabOffset, PhysicalBounds(handle), windowSpace);
    }

    private static void MoveWindowOrigin(nint handle, int x, int y) =>
        NativeMethods.SetWindowPos(handle, nint.Zero, x, y, 0, 0, NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);

    private static NativeMethods.NativeRect PhysicalBounds(nint handle)
    {
        var previousContext = NativeMethods.SetThreadDpiAwarenessContext(NativeMethods.DpiAwarenessContextPerMonitorAwareV2);
        try
        {
            NativeMethods.GetWindowRect(handle, out var bounds);
            return bounds;
        }
        finally
        {
            NativeMethods.SetThreadDpiAwarenessContext(previousContext);
        }
    }

    // The offset is in this app's coordinates, so it is scaled by the bubble's physical size on its
    // current monitor. The tolerance covers one whole coordinate rounded on each side of the mapping.
    internal static bool HoldsGrabbedSpot(PointInt cursor, PointInt grabOffset, NativeMethods.NativeRect physical, NativeMethods.NativeRect windowSpace)
    {
        var scaleX = (physical.Right - physical.Left) / (double)(windowSpace.Right - windowSpace.Left);
        var scaleY = (physical.Bottom - physical.Top) / (double)(windowSpace.Bottom - windowSpace.Top);
        return Math.Abs(physical.Left + (grabOffset.X * scaleX) - cursor.X) <= 1 + (2 * scaleX)
            && Math.Abs(physical.Top + (grabOffset.Y * scaleY) - cursor.Y) <= 1 + (2 * scaleY);
    }

    // The hook reports physical pixels, but this system-DPI-aware app measures the screen in the
    // system scale, so on a monitor scaled differently the two disagree. Windows scales each window
    // by the monitor it is on, so a point is mapped through that window's monitor.
    private static PointInt CursorFromHook(nint lParam)
    {
        var physical = Marshal.PtrToStructure<NativeMethods.NativePoint>(lParam);
        return new PointInt(physical.X, physical.Y);
    }

    private static PointInt CursorInWindowSpace(nint lParam, nint window) =>
        MapToWindowSpace(CursorFromHook(lParam), MonitorSpaceOf(NativeMethods.MonitorFromWindow(window, NativeMethods.MonitorDefaultToNearest)));

    private static List<MonitorSpace> MeasureMonitorSpaces()
    {
        var monitors = new List<nint>();
        NativeMethods.EnumDisplayMonitors(nint.Zero, nint.Zero, (monitor, _, _, _) =>
        {
            monitors.Add(monitor);
            return true;
        }, nint.Zero);
        return monitors.Select(MonitorSpaceOf).OfType<MonitorSpace>().ToList();
    }

    // A monitor, seen once in physical pixels and once in this app's space.
    private static MonitorSpace? MonitorSpaceOf(nint monitor)
    {
        var physical = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        var windowSpace = physical;
        var previousContext = NativeMethods.SetThreadDpiAwarenessContext(NativeMethods.DpiAwarenessContextPerMonitorAwareV2);
        if (previousContext == nint.Zero)
            return null;

        bool measuredPhysical;
        try
        {
            measuredPhysical = NativeMethods.GetMonitorInfoW(monitor, ref physical);
        }
        finally
        {
            NativeMethods.SetThreadDpiAwarenessContext(previousContext);
        }

        return measuredPhysical && NativeMethods.GetMonitorInfoW(monitor, ref windowSpace)
            ? new MonitorSpace(physical.Monitor, windowSpace.Monitor)
            : null;
    }

    private static PointInt MapToWindowSpace(PointInt physical, MonitorSpace? space) =>
        space is { } monitor ? MapToWindowSpace(physical, monitor.Physical, monitor.WindowSpace) : physical;

    internal static PointInt MapToWindowSpace(PointInt physical, NativeMethods.NativeRect physicalMonitor, NativeMethods.NativeRect windowSpaceMonitor) =>
        new(
            MapAxis(physical.X, physicalMonitor.Left, physicalMonitor.Right, windowSpaceMonitor.Left, windowSpaceMonitor.Right),
            MapAxis(physical.Y, physicalMonitor.Top, physicalMonitor.Bottom, windowSpaceMonitor.Top, windowSpaceMonitor.Bottom));

    private static int MapAxis(int value, int physicalStart, int physicalEnd, int windowStart, int windowEnd) =>
        physicalEnd == physicalStart
            ? value
            : windowStart + (int)Math.Floor((double)(value - physicalStart) * (windowEnd - windowStart) / (physicalEnd - physicalStart));

    private bool IsCursorOverIcon(nint lParam) =>
        IsScreenPointOverIcon(CursorInWindowSpace(lParam, new WindowInteropHelper(this).Handle));

    private readonly record struct MonitorSpace(NativeMethods.NativeRect Physical, NativeMethods.NativeRect WindowSpace);

    private void OpenSettings(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

    private void OpenVocabulary(object sender, RoutedEventArgs e) => VocabularyRequested?.Invoke();

    private void OpenTeach(object sender, RoutedEventArgs e) => TeachRequested?.Invoke();

    // Opens the list only after the menu has closed, so the menu's teardown cannot take focus from it.
    private void OpenHistory(object sender, RoutedEventArgs e)
    {
        CloseMenu();
        Dispatcher.BeginInvoke(() => HistoryRequested?.Invoke(), System.Windows.Threading.DispatcherPriority.Background);
    }

    // The card's keys act after the click is done, so nothing the click starts can take focus from the dictation target.
    private void OpenDiagnostics(object sender, RoutedEventArgs e) =>
        Dispatcher.BeginInvoke(() => DiagnosticsRequested?.Invoke(), DispatcherPriority.Background);

    private void OpenMicrophoneSettings(object sender, RoutedEventArgs e) =>
        Dispatcher.BeginInvoke(() => MicrophoneSettingsRequested?.Invoke(), DispatcherPriority.Background);

    private void CopyHeldText(object sender, RoutedEventArgs e)
    {
        if (heldText is { } text)
            CopyRequested?.Invoke(text);
    }

    private void Quit(object sender, RoutedEventArgs e)
    {
        CloseMenu();
        QuitRequested?.Invoke();
    }

    private void StartRecordingIndicator()
    {
        SetRecordingIndicatorVisible(true);
    }

    private void StopRecordingIndicator()
    {
        SetRecordingIndicatorVisible(false);
    }

    private void SetRecordingIndicatorVisible(bool visible)
    {
        if (recordingIndicatorVisible == visible)
            return;

        recordingIndicatorVisible = visible;
        WaveformBars.BeginAnimation(OpacityProperty, null);
        WaveformBars.Opacity = visible ? 1 : 0;
        RecordingIndicatorChanged?.Invoke(visible);
    }

    private void SetWidthAroundCenter(double width) => SetWidthAroundCenter(width, CurrentWorkArea());

    private void SetWidthAroundCenter(double width, DisplayWorkArea workArea)
    {
        if (Math.Abs(Width - width) < double.Epsilon)
            return;

        if (IsVisible)
            Left = CenteredLeft(Left, Width, width);

        Width = width;
        ClampHorizontallyToWorkArea(workArea);
    }

    internal static double CenteredLeft(double left, double previousWidth, double newWidth) =>
        left - ((newWidth - previousWidth) / 2);

    internal static double MapCoordinateToWorkArea(
        double coordinate,
        double bubbleLength,
        double sourceStart,
        double sourceLength,
        double targetStart,
        double targetLength)
    {
        var sourceTravel = Math.Max(0, sourceLength - bubbleLength);
        var relativePosition = sourceTravel == 0 ? 0 : Math.Clamp((coordinate - sourceStart) / sourceTravel, 0, 1);
        var targetTravel = Math.Max(0, targetLength - bubbleLength);
        return targetStart + (targetTravel * relativePosition);
    }

    internal const double ReadyOpacityWithModel = 0.7;
    // Without a model the key is quieter through its outline-only form, not through transparency.
    internal const double ReadyOpacityWithoutModel = 1;

    internal static double OpacityForReadyState(bool modelLoaded) =>
        modelLoaded ? ReadyOpacityWithModel : ReadyOpacityWithoutModel;

    private void ClampHorizontallyToWorkArea(DisplayWorkArea workArea)
    {
        Left = Math.Clamp(Left, workArea.Left, workArea.Right - Width);
    }

    // Re-query the work area when the clamp runs; displays can change in between.
    private void ClampToWorkAreaAfterLayout() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() => ClampToWorkArea(CurrentWorkArea())));

    private void ClampToWorkAreaAfterLayout(DisplayWorkArea workArea)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() => ClampToWorkArea(workArea)));
    }

    private void ClampToWorkArea(DisplayWorkArea workArea)
    {
        Left = Math.Clamp(Left, workArea.Left, workArea.Right - ActualWidth);
        Top = BoundedTop(Top, workArea.Top, workArea.Bottom - WorkAreaBottomClearance, ActualHeight);
    }

    internal static double BoundedTop(double top, double workAreaTop, double workAreaBottom, double height) =>
        Math.Clamp(top, workAreaTop, Math.Max(workAreaTop, workAreaBottom - height));

    private bool IsOffEveryScreen()
    {
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        return !IntersectsAnyWorkArea(Left, Top, width, height, Forms.Screen.AllScreens.Select(WorkAreaFor));
    }

    internal static bool IntersectsAnyWorkArea(double left, double top, double width, double height, IEnumerable<DisplayWorkArea> workAreas)
    {
        if (double.IsNaN(left) || double.IsNaN(top))
            return false;

        foreach (var area in workAreas)
        {
            var visibleWidth = Math.Min(left + width, area.Right) - Math.Max(left, area.Left);
            var visibleHeight = Math.Min(top + height, area.Bottom) - Math.Max(top, area.Top);
            if (visibleWidth >= Math.Min(MinimumVisibleExtent, width) && visibleHeight >= Math.Min(MinimumVisibleExtent, height))
                return true;
        }

        return false;
    }

    private Forms.Screen CurrentScreen()
    {
        var handle = new WindowInteropHelper(this).Handle;
        return handle != nint.Zero ? Forms.Screen.FromHandle(handle) : PrimaryScreen();
    }

    private DisplayWorkArea CurrentWorkArea() => WorkAreaFor(CurrentScreen());

    private static Forms.Screen FindScreen(string? deviceName) =>
        Forms.Screen.AllScreens.FirstOrDefault(screen => string.Equals(screen.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase)) ?? PrimaryScreen();

    private static Forms.Screen PrimaryScreen() => Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];

    // Screens report device pixels while Left and Top are DIPs. PrivateType is system DPI aware,
    // so one scale converts every screen.
    private DisplayWorkArea WorkAreaFor(Forms.Screen screen)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return WorkAreaInDips(screen.WorkingArea, dpi.DpiScaleX, dpi.DpiScaleY);
    }

    internal static DisplayWorkArea WorkAreaInDips(System.Drawing.Rectangle pixels, double dpiScaleX, double dpiScaleY) =>
        new(pixels.Left / dpiScaleX, pixels.Top / dpiScaleY, pixels.Width / dpiScaleX, pixels.Height / dpiScaleY);

    internal readonly record struct PointInt(int X, int Y);

    internal enum DragHookAction
    {
        None,
        Start,
        Move,
        End,
        Recover
    }

    internal readonly record struct DisplayWorkArea(double Left, double Top, double Width, double Height)
    {
        public double Right => Left + Width;
        public double Bottom => Top + Height;
    }

    private enum CardMark
    {
        Listening,
        Working,
        Warning,
        Failure,
        Success
    }

}
