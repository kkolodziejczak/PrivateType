using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using PrivateType.Core;
using Forms = System.Windows.Forms;
using Input = System.Windows.Input;

namespace PrivateType.App;

public partial class DictationBubble : Window
{
    internal const bool RecordingWaveformBlinks = false;
    internal const int SpectrumBarCount = 44;
    internal const string ModelLoadingTitle = "Loading local model…";
    internal const string ModelLoadingHint = "This may take a few seconds. Keep holding to dictate.";
    internal static readonly TimeSpan HintCollapseDelay = TimeSpan.FromMilliseconds(180);

    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WmNchitTest = 0x0084;
    private const int WmExitSizeMove = 0x0232;
    private const int VkLeftButton = 0x01;
    private const double ReadyWidth = 64;
    private const double HintWidth = 330;
    private const double ActiveWidth = 330;
    private const nint HtCaption = 2;
    private readonly DispatcherTimer hintCollapseTimer;
    private readonly AdaptiveAudioMeter adaptiveAudioMeter = new();
    private readonly List<ScaleTransform> waveformScales = [];
    private readonly double[] smoothedSpectrum = new double[SpectrumBarCount];
    private bool active;
    private bool recordingIndicatorVisible;
    private bool recordingVisualsActive;

    public DictationBubble()
    {
        InitializeComponent();
        BubbleShell.ContextMenu.PlacementTarget = BubbleShell;
        BubbleShell.ContextMenu.Placement = PlacementMode.Right;
        BubbleShell.ContextMenu.HorizontalOffset = 8;
        CreateWaveformBars();
        hintCollapseTimer = new DispatcherTimer { Interval = HintCollapseDelay };
        hintCollapseTimer.Tick += CollapseHintsAfterPointerSettles;
    }

    public event Action<string, double, double>? PositionChanged;
    public event Action? SettingsRequested;
    public event Action? QuitRequested;
    public event Action<bool>? RecordingIndicatorChanged;

    public void ShowReady(PortableSettings settings, bool modelLoaded)
    {
        if (!IsVisible)
        {
            Show();
            ApplyPosition(settings);
        }

        active = false;
        recordingVisualsActive = false;
        hintCollapseTimer.Stop();
        StopRecordingIndicator();
        BubbleShell.Opacity = OpacityForReadyState(modelLoaded);
        ApplyReadyVisuals();
        SetWidthAroundCenter(ReadyWidth);
        Hint.Text = DescribeBindings(settings.Shortcuts);
        Hint.Visibility = Visibility.Collapsed;
        WaveformBars.Visibility = Visibility.Collapsed;
        TranscriptViewport.Visibility = Visibility.Collapsed;
    }

    public void MoveToPointerScreen()
    {
        var sourceScreen = CurrentScreen();
        var targetScreen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        if (string.Equals(sourceScreen.DeviceName, targetScreen.DeviceName, StringComparison.OrdinalIgnoreCase))
            return;

        var sourceWorkArea = WorkAreaFor(sourceScreen);
        var targetWorkArea = WorkAreaFor(targetScreen);
        var bubbleWidth = ActualWidth > 0 ? ActualWidth : Width;
        var bubbleHeight = ActualHeight > 0 ? ActualHeight : Height;
        Left = MapCoordinateToWorkArea(Left, bubbleWidth, sourceWorkArea.Left, sourceWorkArea.Width, targetWorkArea.Left, targetWorkArea.Width);
        Top = MapCoordinateToWorkArea(Top, bubbleHeight, sourceWorkArea.Top, sourceWorkArea.Height, targetWorkArea.Top, targetWorkArea.Height);
    }

    public void ShowRecording(RecognitionLanguage language)
    {
        if (recordingVisualsActive)
            return;

        var selectedWorkArea = CurrentWorkArea();
        active = true;
        recordingVisualsActive = true;
        hintCollapseTimer.Stop();
        BubbleShell.Opacity = 1;
        ApplyRecordingVisuals();
        ResetAudioVisuals();
        SetWidthAroundCenter(ActiveWidth, selectedWorkArea);
        StartRecordingIndicator();
        Hint.Visibility = Visibility.Collapsed;
        WaveformBars.Visibility = Visibility.Visible;
        Transcript.FontSize = 14;
        Transcript.Opacity = 0.92;
        SetTranscript(string.Empty);
        TranscriptViewport.Visibility = Visibility.Visible;
        ClampToWorkAreaAfterLayout(selectedWorkArea);
    }

    public void ShowModelLoading()
    {
        active = true;
        recordingVisualsActive = false;
        hintCollapseTimer.Stop();
        StopRecordingIndicator();
        BubbleShell.Opacity = 1;
        ApplyExpandedVisuals(ModelLoadingTitle, "ColorAccent900", "ColorAccent300", "ColorAccent300");
        SetWidthAroundCenter(ActiveWidth);
        Hint.Text = ModelLoadingHint;
        Hint.Visibility = Visibility.Visible;
        WaveformBars.Visibility = Visibility.Collapsed;
        TranscriptViewport.Visibility = Visibility.Collapsed;
        ClampToWorkAreaAfterLayout();
    }

    public void ShowTranscript(string transcript)
    {
        SetTranscript(transcript);
    }

    public void ShowAudioMeter(AudioMeter meter)
    {
        if (!recordingVisualsActive)
            return;

        meter = adaptiveAudioMeter.Normalize(meter);
        var level = Math.Clamp(meter.Level, 0, 1);
        for (var index = 0; index < waveformScales.Count; index++)
        {
            var target = index < meter.Spectrum.Count ? Math.Clamp(meter.Spectrum[index], 0, 1) : 0;
            smoothedSpectrum[index] += (target - smoothedSpectrum[index]) * 0.68;
            waveformScales[index].ScaleY = Math.Max(0.08, smoothedSpectrum[index]);
        }

        WaveformBars.Opacity = 0.7 + (level * 0.3);
        RecordingPulseRing.Opacity = level < 0.025 ? 0 : 0.12 + (level * 0.5);
        var scale = 1 + (level * 0.24);
        RecordingPulseScale.ScaleX = scale;
        RecordingPulseScale.ScaleY = scale;
    }

    public void ShowFinalizing()
    {
        recordingVisualsActive = false;
        StopRecordingIndicator();
        StopRecordingPulse();
    }

    public void ShowCancellation(string message)
    {
        active = false;
        recordingVisualsActive = false;
        hintCollapseTimer.Stop();
        StopRecordingIndicator();
        BubbleShell.Opacity = 1;
        ApplyCancelledVisuals();
        SetWidthAroundCenter(ActiveWidth);
        Hint.Visibility = Visibility.Collapsed;
        WaveformBars.Visibility = Visibility.Collapsed;
        Transcript.FontSize = 13;
        Transcript.Opacity = 0.6;
        SetTranscript(message);
        TranscriptViewport.Visibility = Visibility.Visible;
        ClampToWorkAreaAfterLayout();
    }

    public void ShowError(string message)
    {
        active = true;
        recordingVisualsActive = false;
        hintCollapseTimer.Stop();
        StopRecordingIndicator();
        BubbleShell.Opacity = 1;
        ApplyErrorVisuals();
        SetWidthAroundCenter(ActiveWidth);
        Hint.Visibility = Visibility.Collapsed;
        WaveformBars.Visibility = Visibility.Collapsed;
        Transcript.FontSize = 13;
        Transcript.Opacity = 0.6;
        SetTranscript(message);
        TranscriptViewport.Visibility = Visibility.Visible;
        ClampToWorkAreaAfterLayout();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = (HwndSource)PresentationSource.FromVisual(this)!;
        source.AddHook(WindowMessageHook);
        var handle = new WindowInteropHelper(this).Handle;
        var extendedStyle = NativeMethods.GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        NativeMethods.SetWindowLongPtr(handle, GwlExStyle, (nint)(extendedStyle | WsExNoActivate));
    }

    private nint WindowMessageHook(nint handle, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmNchitTest && !active && (NativeMethods.GetKeyState(VkLeftButton) & 0x8000) != 0)
        {
            handled = true;
            return HtCaption;
        }

        if (message == WmExitSizeMove)
            ReportPosition();

        return nint.Zero;
    }

    private void CreateWaveformBars()
    {
        for (var index = 0; index < SpectrumBarCount; index++)
        {
            var scale = new ScaleTransform(1, 0.08);
            var bar = new System.Windows.Shapes.Rectangle
            {
                Width = 1.5,
                Height = 16,
                RadiusX = 0.75,
                RadiusY = 0.75,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["ColorAccent400"],
                RenderTransform = scale,
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5)
            };
            waveformScales.Add(scale);
            WaveformBars.Children.Add(bar);
        }
    }

    private void ApplyReadyVisuals()
    {
        BubbleShell.CornerRadius = new CornerRadius(32);
        BubbleShell.Padding = new Thickness(13);
        BubbleHeader.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        BubbleHeader.Width = 36;
        StatusTitle.Visibility = Visibility.Collapsed;
        IconTile.Background = System.Windows.Media.Brushes.Transparent;
        IconTile.BorderBrush = System.Windows.Media.Brushes.Transparent;
        IconOutline.Fill = Brush("ColorAccent800");
        IconOutline.Stroke = Brush("ColorDivider");
        SetIconBars("ColorAccent300");
    }

    private void ApplyRecordingVisuals()
    {
        ApplyExpandedVisuals("Listening", "ColorAccent700", "ColorAccent", "ColorAccent100");
    }

    private void ApplyCancelledVisuals()
    {
        ApplyExpandedVisuals("Cancelled", "ColorNeutral800", "ColorDivider", "ColorNeutral400");
    }

    private void ApplyErrorVisuals()
    {
        ApplyExpandedVisuals("Error", "ColorAccent900", "ColorAccent300", "ColorAccent300");
    }

    private void ApplyExpandedVisuals(string title, string tileBrush, string strokeBrush, string barBrush)
    {
        StopRecordingPulse();
        BubbleShell.CornerRadius = new CornerRadius(14);
        BubbleShell.Padding = new Thickness(17);
        BubbleHeader.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        BubbleHeader.Width = double.NaN;
        StatusTitle.Text = title;
        StatusTitle.Visibility = Visibility.Visible;
        IconTile.Background = System.Windows.Media.Brushes.Transparent;
        IconTile.BorderBrush = System.Windows.Media.Brushes.Transparent;
        IconOutline.Fill = Brush(tileBrush);
        IconOutline.Stroke = Brush(strokeBrush);
        SetIconBars(barBrush);
    }

    private void SetIconBars(string brush)
    {
        IconBarOne.Fill = Brush(brush);
        IconBarTwo.Fill = Brush(brush);
        IconBarThree.Fill = Brush(brush);
    }

    private void ResetAudioVisuals()
    {
        adaptiveAudioMeter.Reset();
        Array.Fill(smoothedSpectrum, 0.08);
        foreach (var scale in waveformScales)
            scale.ScaleY = 0.08;

        StopRecordingPulse();
    }

    private void StopRecordingPulse()
    {
        RecordingPulseRing.Opacity = 0;
        RecordingPulseScale.ScaleX = 1;
        RecordingPulseScale.ScaleY = 1;
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

    private void ExpandHints(object sender, Input.MouseEventArgs e)
    {
        if (!active)
        {
            hintCollapseTimer.Stop();
            ApplyExpandedVisuals("PrivateType", "ColorAccent800", "ColorDivider", "ColorAccent300");
            SetWidthAroundCenter(HintWidth);
            Hint.Visibility = Visibility.Visible;
        }
    }

    private void CollapseHints(object sender, Input.MouseEventArgs e)
    {
        if (!active)
            hintCollapseTimer.Start();
    }

    private void CollapseHintsAfterPointerSettles(object? sender, EventArgs e)
    {
        hintCollapseTimer.Stop();
        if (!ShouldCollapseHints(active, BubbleShell.IsMouseOver))
            return;

        ApplyReadyVisuals();
        SetWidthAroundCenter(ReadyWidth);
        Hint.Visibility = Visibility.Collapsed;
    }

    private void StartDrag(object sender, Input.MouseButtonEventArgs e)
    {
        if (active || e.LeftButton != Input.MouseButtonState.Pressed)
            return;

        DragMove();
    }

    protected override void OnClosed(EventArgs e)
    {
        hintCollapseTimer.Stop();
        base.OnClosed(e);
    }

    private void OpenSettings(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

    private void Quit(object sender, RoutedEventArgs e) => QuitRequested?.Invoke();

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

    internal static double OpacityForReadyState(bool modelLoaded) => modelLoaded ? 1 : 0.45;

    private void ClampHorizontallyToWorkArea(DisplayWorkArea workArea)
    {
        Left = Math.Clamp(Left, workArea.Left, workArea.Right - Width);
    }

    private void ClampToWorkAreaAfterLayout() => ClampToWorkAreaAfterLayout(CurrentWorkArea());

    private void ClampToWorkAreaAfterLayout(DisplayWorkArea workArea)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() => ClampToWorkArea(workArea)));
    }

    private void ClampToWorkArea(DisplayWorkArea workArea)
    {
        Left = Math.Clamp(Left, workArea.Left, workArea.Right - ActualWidth);
        Top = BoundedTop(Top, workArea.Top, workArea.Bottom, ActualHeight);
    }

    internal static double BoundedTop(double top, double workAreaTop, double workAreaBottom, double height) =>
        Math.Clamp(top, workAreaTop, Math.Max(workAreaTop, workAreaBottom - height));

    private Forms.Screen CurrentScreen()
    {
        var handle = new WindowInteropHelper(this).Handle;
        return handle != nint.Zero ? Forms.Screen.FromHandle(handle) : PrimaryScreen();
    }

    private DisplayWorkArea CurrentWorkArea() => WorkAreaFor(CurrentScreen());

    private static Forms.Screen FindScreen(string? deviceName) =>
        Forms.Screen.AllScreens.FirstOrDefault(screen => string.Equals(screen.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase)) ?? PrimaryScreen();

    private static Forms.Screen PrimaryScreen() => Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];

    private static DisplayWorkArea WorkAreaFor(Forms.Screen screen)
    {
        var workArea = screen.WorkingArea;
        return new DisplayWorkArea(workArea.Left, workArea.Top, workArea.Width, workArea.Height);
    }

    private readonly record struct DisplayWorkArea(double Left, double Top, double Width, double Height)
    {
        public double Right => Left + Width;
        public double Bottom => Top + Height;
    }

    internal static bool ShouldCollapseHints(bool isActive, bool isPointerOverBubble) => !isActive && !isPointerOverBubble;

    private static string DescribeBindings(IReadOnlyList<ShortcutBinding> bindings) =>
        string.Join("\n", HotkeyCatalog.FromBindings(bindings).Select(binding => $"{LanguageLabel(binding.Language)} — {binding.Label}"));

    private static string LanguageLabel(RecognitionLanguage language) => language switch
    {
        RecognitionLanguage.Polish => "Polish",
        RecognitionLanguage.English => "English",
        _ => "Automatic"
    };
}
