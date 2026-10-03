using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PrivateType.App;
using PrivateType.Core;
using Input = System.Windows.Input;

// Uses synthetic sentences only; never real dictated content.
internal static class HistoryProbe
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    public static void Run(string outputDirectory)
    {
        VerifyListStates(outputDirectory);
        VerifyManyEntries(outputDirectory);
        VerifyEmptyStates(outputDirectory);
        VerifySettingsSection(outputDirectory);
        VerifyFocusDismissal();
        Console.WriteLine("PASS: Recent dictations lists newest first with age, language and a Not inserted badge, pastes with Enter or click, removes with Delete or ×, clears all, scrolls when long, explains empty and off states, stays open when Windows refuses it the foreground, closes once the user moves on, and Settings saves the retention and clipboard-history choices.");
    }

    private const nint PreviousApp = 100, OtherApp = 300;

    // Windows decides the real foreground, so the probe supplies it. Activation events may or may
    // not arrive in a background probe; the list's foreground watch must decide either way.
    private static void VerifyFocusDismissal()
    {
        var clock = new FixedClock(Now);
        var history = new DictationHistory(clock);
        history.Add(new FinalizedDictation("Synthetic sentence for the focus probe.", "en-US"));

        var foreground = PreviousApp;
        var window = new DictationHistoryWindow(history, clock, () => foreground);
        var other = new Window { Width = 120, Height = 80, ShowInTaskbar = false, ShowActivated = false };
        window.Show();
        other.Show();
        try
        {
            Flush(window);
            other.Activate();
            Pump(TimeSpan.FromMilliseconds(600));
            Require(window.IsVisible && !window.HeldForeground, "A list Windows never gave the foreground must stay open after losing activation.");

            foreground = new WindowInteropHelper(window).Handle;
            window.Activate();
            Pump(TimeSpan.FromMilliseconds(600));
            Require(window.IsVisible && window.HeldForeground, "The list notices when it holds the foreground.");

            foreground = PreviousApp;
            other.Activate();
            Pump(TimeSpan.FromMilliseconds(600));
            Require(!window.IsVisible && window.CloseReason == "focus-lost", "The list closes once it loses the foreground it held.");
        }
        finally
        {
            window.Close();
            other.Close();
        }

        foreground = PreviousApp;
        var unfocused = new DictationHistoryWindow(history, clock, () => foreground);
        unfocused.Show();
        try
        {
            Pump(TimeSpan.FromMilliseconds(600));
            Require(unfocused.IsVisible, "The list waits while the previous app keeps the foreground.");
            foreground = OtherApp;
            Pump(TimeSpan.FromMilliseconds(600));
            Require(!unfocused.IsVisible && unfocused.CloseReason == "focus-lost", "Switching to another app closes a list that never held the foreground.");
        }
        finally
        {
            unfocused.Close();
        }
    }

    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void VerifyListStates(string outputDirectory)
    {
        var clock = new FixedClock(Now.AddDays(-1));
        var history = new DictationHistory(clock);
        history.Add(new FinalizedDictation("Order more filament before the weekend print run.", "en-US"));
        clock.Now = Now.AddMinutes(-42);
        history.Add(new FinalizedDictation("Synthetic Polish sample sentence for the layout probe.", "pl-PL"));
        clock.Now = Now.AddMinutes(-3);
        history.Add(new FinalizedDictation(string.Join(' ', Enumerable.Repeat("This long synthetic dictation keeps going so the preview has to wrap and then stop after three lines.", 4)), "en-US") { Inserted = false });
        clock.Now = Now;
        history.Add(new FinalizedDictation("Short note.", "auto"));

        DictationHistoryEntry? chosen = null;
        var window = CreateWindow(history, clock);
        window.Chosen += entry => chosen = entry;
        window.Show();
        try
        {
            Flush(window);
            var list = Find<ListBox>(window, "EntriesList");
            Require(window.VisibleCount == 4 && list.SelectedIndex == 0, "All entries must be listed with the newest selected.");
            var items = list.Items.Cast<DictationHistoryItem>().ToArray();
            Require(items[0].Preview == "Short note." && items[0].Details.StartsWith("Just now", StringComparison.Ordinal), "The newest entry must come first.");
            Require(items.Count(item => item.ShowNotInserted) == 1 && items[1].ShowNotInserted, "Only the dropped dictation shows Not inserted.");
            Require(items[3].Details.StartsWith("1 Oct", StringComparison.Ordinal), "Entries from another day show their date.");
            foreach (var index in Enumerable.Range(0, items.Length))
            {
                var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(index);
                Require(container.ActualHeight is > 40 and < 110, $"Entry {index} must stay compact (was {container.ActualHeight:0}).");
                VerifyContained(window, container);
                Require(UIElementAutomationPeer.CreatePeerForElement(container)?.GetName() == items[index].Preview, "Each entry needs its text as the accessible name.");
            }
            Require(Find<Button>(window, "ClearAllButton").IsEnabled, "Clear all is available when entries exist.");
            Capture(window, outputDirectory, "history-list.png");

            Press(window, Input.Key.Delete);
            Require(window.VisibleCount == 3 && list.SelectedIndex == 0 && history.Snapshot().All(entry => entry.Text != "Short note."),
                "Delete removes the selected entry and keeps a selection.");

            var removeButton = Descendants(list.ItemContainerGenerator.ContainerFromIndex(1)).OfType<Button>().Single();
            Click(removeButton);
            Require(window.VisibleCount == 2 && chosen is null && window.IsVisible, "× removes one entry without pasting it.");

            list.SelectedIndex = 1;
            Press(window, Input.Key.Enter);
            Require(chosen?.Text == "Order more filament before the weekend print run." && !window.IsVisible, "Enter pastes the selected entry and closes the list.");
        }
        finally
        {
            window.Close();
        }

        var clickWindow = CreateWindow(history, clock);
        DictationHistoryEntry? clicked = null;
        clickWindow.Chosen += entry => clicked = entry;
        clickWindow.Show();
        try
        {
            Flush(clickWindow);
            var list = Find<ListBox>(clickWindow, "EntriesList");
            var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
            container.RaiseEvent(new Input.MouseButtonEventArgs(Input.Mouse.PrimaryDevice, 0, Input.MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent, Source = container });
            Require(clicked is not null && !clickWindow.IsVisible, "Clicking an entry pastes it.");
        }
        finally
        {
            clickWindow.Close();
        }
    }

    private static void VerifyManyEntries(string outputDirectory)
    {
        var clock = new FixedClock(Now);
        var history = new DictationHistory(clock);
        for (var index = 0; index < DictationHistory.Capacity; index++)
            history.Add(new FinalizedDictation($"Synthetic dictation number {index + 1} with a few extra words.", "en-US"));

        var window = CreateWindow(history, clock);
        window.Show();
        try
        {
            Flush(window);
            var scroll = Descendants(Find<ListBox>(window, "EntriesList")).OfType<ScrollViewer>().First();
            Require(window.ActualHeight <= window.MaxHeight + 1, "A long history must not grow past the maximum height.");
            Require(scroll.ScrollableHeight > 0, "A long history must scroll.");
            Capture(window, outputDirectory, "history-many.png");

            Click(Find<Button>(window, "ClearAllButton"));
            Require(window.VisibleCount == 0 && history.Snapshot().Count == 0 && window.EmptyMessage == DictationHistoryWindow.EmptyListText
                && !Find<Button>(window, "ClearAllButton").IsEnabled, "Clear all empties the history and shows the empty state.");
        }
        finally
        {
            window.Close();
        }
    }

    private static void VerifyEmptyStates(string outputDirectory)
    {
        var clock = new FixedClock(Now);
        foreach (var (retention, expected, file) in new[]
        {
            (DictationHistoryRetentions.UntilExit, DictationHistoryWindow.EmptyListText, "history-empty.png"),
            (DictationHistoryRetentions.Off, DictationHistoryWindow.OffText, "history-off.png")
        })
        {
            var window = CreateWindow(new DictationHistory(clock) { Retention = retention }, clock);
            window.Show();
            try
            {
                Flush(window);
                Require(window.EmptyMessage == expected && !Find<Button>(window, "ClearAllButton").IsEnabled, $"{retention} must explain why the list is empty.");
                VerifyContained(window, Find<TextBlock>(window, "EmptyText"));
                Capture(window, outputDirectory, file);
                Press(window, Input.Key.Escape);
                Require(!window.IsVisible, "Esc closes the list.");
            }
            finally
            {
                window.Close();
            }
        }
    }

    private static void VerifySettingsSection(string outputDirectory)
    {
        var window = new SettingsWindow(PortableSettings.Default, [new MicrophoneOption("default", "System default microphone")], ProbeModelStore.Empty) { ShowInTaskbar = false, Height = 800 };
        window.Show();
        try
        {
            Flush(window);
            var retention = Find<ComboBox>(window, "HistoryRetentionBox");
            var clipboard = Find<CheckBox>(window, "ClipboardHistoryCheckBox");
            var hint = Find<TextBlock>(window, "HistoryHint");
            var clipboardHint = Find<TextBlock>(window, "ClipboardHistoryHint");
            Require(Equals(retention.SelectedValue, DictationHistoryRetentions.UntilExit) && retention.Items.Count == 4, "Retention defaults to until exit and offers four choices.");
            Require(clipboard.IsChecked == false && clipboard.IsEnabled, "Clipboard history starts off and is available while pasting.");
            Require(hint.Text.Contains("Win+Shift+V", StringComparison.Ordinal), "The hint names the shortcut.");
            retention.BringIntoView();
            clipboardHint.BringIntoView();
            Flush(window);
            VerifyContained(window, clipboardHint);
            var label = Descendants(clipboard).OfType<TextBlock>().Single();
            var section = (FrameworkElement)clipboard.Parent;
            Require(label.TranslatePoint(new Point(label.ActualWidth, 0), section).X <= section.ActualWidth + 1, "The clipboard-history label must not be clipped.");
            Capture(window, outputDirectory, "settings-history.png");

            clipboard.IsChecked = true;
            retention.SelectedValue = DictationHistoryRetentions.Off;
            Flush(window);
            Require(hint.Text.Contains("stays with Windows", StringComparison.Ordinal) && clipboardHint.Text.Contains("Cloud clipboard still skips", StringComparison.Ordinal),
                "Hints follow the chosen options.");
            Capture(window, outputDirectory, "settings-history-off.png");

            Find<ComboBox>(window, "InsertionModeBox").SelectedValue = TextInsertionModes.Type;
            Flush(window);
            Require(!clipboard.IsEnabled && clipboardHint.Text.Contains("pasting", StringComparison.Ordinal), "Clipboard history is disabled while typing characters.");
            Capture(window, outputDirectory, "settings-history-typing.png");

        }
        finally
        {
            window.Close();
        }

        VerifySettingsSave();
    }

    private static void VerifySettingsSave()
    {
        var window = new SettingsWindow(PortableSettings.Default, [new MicrophoneOption("default", "System default microphone")], ProbeModelStore.Empty) { ShowInTaskbar = false };
        Exception? failure = null;
        window.ContentRendered += (_, _) => window.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                Find<ComboBox>(window, "HistoryRetentionBox").SelectedValue = DictationHistoryRetentions.OneHour;
                Find<CheckBox>(window, "ClipboardHistoryCheckBox").IsChecked = true;
                Click(Find<Button>(window, "SaveSettingsButton"));
            }
            catch (Exception exception)
            {
                failure = exception;
                window.Close();
            }
        }, DispatcherPriority.ApplicationIdle);
        var result = window.ShowDialog();
        if (failure is not null)
            throw failure;
        Require(result == true && window.SavedSettings is { DictationHistory: DictationHistoryRetentions.OneHour, IncludeInClipboardHistory: true },
            "Save keeps the history choices.");
    }

    // Not activated, and the foreground never moves, so the list stays open while other probe windows open.
    private static DictationHistoryWindow CreateWindow(DictationHistory history, TimeProvider clock) =>
        new(history, clock, () => PreviousApp) { ShowActivated = false };

    private static void Press(Window window, Input.Key key)
    {
        window.RaiseEvent(new Input.KeyEventArgs(Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, key)
        {
            RoutedEvent = Input.Keyboard.PreviewKeyDownEvent
        });
        if (window.IsVisible)
            Flush(window);
    }

    private static void Click(Button button)
    {
        var window = Window.GetWindow(button);
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        if (window?.IsVisible == true)
            Flush(window);
    }

    private static T Find<T>(Window window, string name) where T : FrameworkElement =>
        window.FindName(name) as T ?? throw new InvalidOperationException($"Missing control {name}.");

    private static void VerifyContained(Window window, FrameworkElement control)
    {
        var origin = control.TranslatePoint(new Point(), window);
        Require(control.IsVisible && control.ActualWidth > 0 && control.ActualHeight > 0
            && origin.X >= 0 && origin.Y >= 0
            && origin.X + control.ActualWidth <= window.ActualWidth + 1
            && origin.Y + control.ActualHeight <= window.ActualHeight + 1,
            $"{control.Name} must remain visible inside the window.");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static void Capture(FrameworkElement element, string directory, string name)
    {
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(element.ActualWidth)),
            Math.Max(1, (int)Math.Ceiling(element.ActualHeight)), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, name));
        encoder.Save(output);
    }

    private static void Flush(Window window)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
