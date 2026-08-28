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

public partial class DictationBubble : Window
{
    internal const bool RecordingWaveformBlinks = false;
    internal const int SpectrumBarCount = 44;
    internal const string ModelLoadingTitle = "Loading local model…";
    internal const string ModelLoadingHint = "This may take a few seconds. Keep holding to dictate.";

    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WmNchitTest = 0x0084;
    private const int WmExitSizeMove = 0x0232;
    private const double ReadyWidth = 64;
    private const double ActiveWidth = 330;
    private const double WorkAreaBottomClearance = 8;
    private const nint HtClient = 1;
    private const nint HtTransparent = 0x20;
    private readonly AdaptiveAudioMeter adaptiveAudioMeter = new();
    private readonly List<ScaleTransform> waveformScales = [];
    private readonly double[] smoothedSpectrum = new double[SpectrumBarCount];
    private bool active;
    private bool recordingIndicatorVisible;
    private bool recordingVisualsActive;
    private bool dragActive;
    private int dragStartCursorX, dragStartCursorY;
    private double dragStartLeft, dragStartTop;
    private double dragDpiScaleX = 1, dragDpiScaleY = 1;
    private readonly NativeMethods.MouseHookProc mouseHookProc;
    private nint mouseHook;

    public DictationBubble()
    {
        InitializeComponent();
        mouseHookProc = OnMouseHook;
        BubbleShell.ContextMenu.PlacementTarget = BubbleShell;
        BubbleShell.ContextMenu.Placement = PlacementMode.Right;
        BubbleShell.ContextMenu.HorizontalOffset = 8;
        CreateWaveformBars();
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
        ClampToWorkAreaAfterLayout();
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

            if (IsPointerOverIcon(lParam))
            {
                // The icon stays interactive and draggable.
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

    private bool IsPointerOverIcon(nint lParam) => IsScreenPointOverIcon(CursorScreenPosition(lParam));

    private bool IsScreenPointOverIcon(PointInt cursor)
    {
        if (IconTile.ActualWidth <= 0 || IconTile.ActualHeight <= 0)
            return false;

        var local = IconTile.PointFromScreen(new System.Windows.Point(cursor.X, cursor.Y));
        return local.X >= 0 && local.X < IconTile.ActualWidth
            && local.Y >= 0 && local.Y < IconTile.ActualHeight;
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

    protected override void OnClosed(EventArgs e)
    {
        if (mouseHook != nint.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(mouseHook);
            mouseHook = nint.Zero;
        }

        base.OnClosed(e);
    }

    private nint OnMouseHook(int code, nint wParam, nint lParam)
    {
        if (code < 0)
            return NativeMethods.CallNextHookEx(mouseHook, code, wParam, lParam);

        switch (ClassifyDragHookAction(dragActive, wParam))
        {
            case DragHookAction.Start when active || !IsCursorOverIcon(lParam):
                return NativeMethods.CallNextHookEx(mouseHook, code, wParam, lParam);
            case DragHookAction.Start:
            {
                var p = CursorFromHook(lParam);
                dragStartCursorX = p.X;
                dragStartCursorY = p.Y;
                dragStartLeft = Left;
                dragStartTop = Top;
                var dpi = VisualTreeHelper.GetDpi(this);
                dragDpiScaleX = dpi.DpiScaleX;
                dragDpiScaleY = dpi.DpiScaleY;
                dragActive = true;
                return 1;
            }
            case DragHookAction.Move:
            {
                var p = CursorFromHook(lParam);
                var offset = CursorDeltaInDips(
                    dragStartCursorX,
                    dragStartCursorY,
                    p.X,
                    p.Y,
                    dragDpiScaleX,
                    dragDpiScaleY);
                Left = dragStartLeft + offset.X;
                Top = dragStartTop + offset.Y;
                break;
            }
            case DragHookAction.End:
                dragActive = false;
                ReportPosition();
                // We suppressed the matching button-down, so suppress the up too.
                return 1;
        }

        return NativeMethods.CallNextHookEx(mouseHook, code, wParam, lParam);
    }

    internal static DragHookAction ClassifyDragHookAction(bool dragActive, nint message) =>
        (dragActive, message) switch
        {
            (false, NativeMethods.WmLButtonDown) => DragHookAction.Start,
            (true, NativeMethods.WmMouseMove) => DragHookAction.Move,
            (true, NativeMethods.WmLButtonUp) => DragHookAction.End,
            _ => DragHookAction.None
        };

    internal static Vector CursorDeltaInDips(
        int startX,
        int startY,
        int currentX,
        int currentY,
        double dpiScaleX,
        double dpiScaleY) =>
        new((currentX - startX) / dpiScaleX, (currentY - startY) / dpiScaleY);

    private static NativeMethods.NativePoint CursorFromHook(nint lParam) =>
        Marshal.PtrToStructure<NativeMethods.NativePoint>(lParam);

    private bool IsCursorOverIcon(nint lParam)
    {
        var cursor = CursorFromHook(lParam);
        return IsScreenPointOverIcon(new PointInt(cursor.X, cursor.Y));
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

    internal const double ReadyOpacityWithModel = 0.7;
    internal const double ReadyOpacityWithoutModel = 0.4;

    internal static double OpacityForReadyState(bool modelLoaded) =>
        modelLoaded ? ReadyOpacityWithModel : ReadyOpacityWithoutModel;

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
        Top = BoundedTop(Top, workArea.Top, workArea.Bottom - WorkAreaBottomClearance, ActualHeight);
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

    internal readonly record struct PointInt(int X, int Y);

    internal enum DragHookAction
    {
        None,
        Start,
        Move,
        End
    }

    private readonly record struct DisplayWorkArea(double Left, double Top, double Width, double Height)
    {
        public double Right => Left + Width;
        public double Bottom => Top + Height;
    }

    private static string DescribeBindings(IReadOnlyList<ShortcutBinding> bindings) =>
        string.Join("\n", HotkeyCatalog.FromBindings(bindings).Select(binding => $"{LanguageLabel(binding.Language)} — {binding.Label}"));

    private static string LanguageLabel(RecognitionLanguage language) => language switch
    {
        RecognitionLanguage.Polish => "Polish",
        RecognitionLanguage.English => "English",
        _ => "Automatic"
    };
}
