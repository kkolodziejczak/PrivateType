using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using PrivateType.Core;
using Forms = System.Windows.Forms;
using Input = System.Windows.Input;

namespace PrivateType.App;

// A Win+V style list of recent dictations. Choosing one closes the window and reports it through
// Chosen; the caller pastes it into the window that was active before this one opened.
public partial class DictationHistoryWindow : Window
{
    private readonly DictationHistory history;
    private readonly TimeProvider clock;
    private bool closing;

    internal DictationHistoryWindow(DictationHistory history, TimeProvider clock)
    {
        InitializeComponent();
        this.history = history;
        this.clock = clock;
        Refresh(selectIndex: 0);
        Loaded += (_, _) =>
        {
            CenterOnPointerScreen();
            FocusSelection();
        };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(CenterOnPointerScreen);
        Deactivated += (_, _) => CloseOnce();
        Closing += (_, _) => closing = true;
        // A dictation can finish, or an entry expire, while the list is open.
        history.Changed += RefreshAfterChange;
        Closed += (_, _) => history.Changed -= RefreshAfterChange;
    }

    private void RefreshAfterChange()
    {
        var selected = (EntriesList.SelectedItem as DictationHistoryItem)?.Entry.Id;
        var entries = history.Snapshot();
        var index = entries.ToList().FindIndex(entry => entry.Id == selected);
        Refresh(index < 0 ? 0 : index);
    }

    public event Action<DictationHistoryEntry>? Chosen;

    internal const string OffText = "Recent dictations are off. Turn them on in Settings → Recent dictations.";
    internal const string EmptyListText = "No dictations yet. Finished dictations appear here, so you can paste one again after switching windows.";

    internal int VisibleCount => EntriesList.Items.Count;

    internal string? EmptyMessage => EmptyText.Visibility == Visibility.Visible ? EmptyText.Text : null;

    internal void Refresh(int selectIndex)
    {
        var now = clock.GetUtcNow();
        var items = history.Snapshot().Select(entry => DictationHistoryItem.From(entry, now, TimeZoneInfo.Local)).ToArray();
        EntriesList.ItemsSource = items;
        EntriesList.Visibility = items.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Visibility = items.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        KeyHintText.Visibility = EntriesList.Visibility;
        EmptyText.Text = history.IsEnabled ? EmptyListText : OffText;
        ClearAllButton.IsEnabled = items.Length > 0;
        if (items.Length > 0)
            EntriesList.SelectedIndex = Math.Clamp(selectIndex, 0, items.Length - 1);
    }

    private void HandleKeys(object sender, Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Input.Key.Escape:
                CloseOnce();
                e.Handled = true;
                break;
            case Input.Key.Enter when EntriesList.SelectedItem is DictationHistoryItem item:
                Choose(item);
                e.Handled = true;
                break;
            case Input.Key.Delete when EntriesList.SelectedItem is DictationHistoryItem item:
                Remove(item);
                e.Handled = true;
                break;
        }
    }

    private void ChooseClickedEntry(object sender, Input.MouseButtonEventArgs e)
    {
        if (IsInsideButton(e.OriginalSource as DependencyObject))
            return;

        if (((FrameworkElement)sender).DataContext is DictationHistoryItem item)
            Choose(item);
    }

    private void RemoveEntry(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is DictationHistoryItem item)
            Remove(item);
    }

    private void Remove(DictationHistoryItem item)
    {
        var index = EntriesList.Items.IndexOf(item);
        history.Remove(item.Entry.Id);
        Refresh(index);
        FocusSelection();
    }

    private void ClearAll(object sender, RoutedEventArgs e)
    {
        history.Clear();
        Refresh(selectIndex: 0);
    }

    private void Choose(DictationHistoryItem item)
    {
        CloseOnce();
        Chosen?.Invoke(item.Entry);
    }

    private void CloseWindow(object sender, RoutedEventArgs e) => CloseOnce();

    private void CloseOnce()
    {
        if (closing)
            return;

        closing = true;
        Close();
    }

    private void FocusSelection()
    {
        if (EntriesList.SelectedIndex < 0)
        {
            Focus();
            return;
        }

        EntriesList.UpdateLayout();
        EntriesList.ScrollIntoView(EntriesList.SelectedItem);
        if (EntriesList.ItemContainerGenerator.ContainerFromIndex(EntriesList.SelectedIndex) is UIElement container)
            container.Focus();
    }

    // Opened from a global shortcut, so another app owns the foreground and Windows refuses a plain
    // Activate. Sharing that app's input queue for the call lets the list take keyboard focus.
    internal void TakeForeground()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == nint.Zero)
            return;

        var foregroundThread = NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out _);
        var currentThread = NativeMethods.GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != currentThread && NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            NativeMethods.BringWindowToTop(handle);
            NativeMethods.SetForegroundWindow(handle);
            Activate();
        }
        finally
        {
            if (attached)
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
        }
        FocusSelection();
    }

    // Centered horizontally, a third of the way down the work area of the monitor under the pointer.
    // Positioned in physical pixels so mixed-DPI monitors cannot misplace it.
    private void CenterOnPointerScreen()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == nint.Zero || !NativeMethods.GetCursorPos(out var pointer) || !NativeMethods.GetWindowRect(handle, out var bounds))
            return;

        var workArea = Forms.Screen.FromPoint(new System.Drawing.Point(pointer.X, pointer.Y)).WorkingArea;
        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        var x = workArea.Left + Math.Max(0, (workArea.Width - width) / 2);
        var y = workArea.Top + Math.Max(0, Math.Min((workArea.Height - height) / 3, workArea.Height - height));
        NativeMethods.SetWindowPos(handle, nint.Zero, x, y, 0, 0, NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
    }

    private static bool IsInsideButton(DependencyObject? element)
    {
        for (var current = element; current is not null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase)
                return true;
            if (current is System.Windows.Controls.ListBoxItem)
                return false;
        }

        return false;
    }
}

public sealed record DictationHistoryItem(DictationHistoryEntry Entry, string Preview, string Details, bool ShowNotInserted)
{
    internal static DictationHistoryItem From(DictationHistoryEntry entry, DateTimeOffset now, TimeZoneInfo timeZone)
    {
        var preview = string.Join(' ', entry.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var language = RecognitionLocaleCatalog.IsSupported(entry.LocaleCode)
            ? RecognitionLocaleCatalog.Get(entry.LocaleCode).DisplayName
            : entry.LocaleCode;
        return new DictationHistoryItem(entry, preview, $"{Age(entry.FinishedAt, now, timeZone)} · {language}", !entry.Inserted);
    }

    internal static string Age(DateTimeOffset finishedAt, DateTimeOffset now, TimeZoneInfo timeZone)
    {
        var age = now - finishedAt;
        if (age < TimeSpan.FromMinutes(1))
            return "Just now";
        if (age < TimeSpan.FromHours(1))
            return $"{(int)age.TotalMinutes} min ago";

        var local = TimeZoneInfo.ConvertTime(finishedAt, timeZone);
        var today = TimeZoneInfo.ConvertTime(now, timeZone).Date;
        return local.Date == today
            ? local.ToString("HH:mm", CultureInfo.InvariantCulture)
            : local.ToString("d MMM, HH:mm", CultureInfo.InvariantCulture);
    }
}
