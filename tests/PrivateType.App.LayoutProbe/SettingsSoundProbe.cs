using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PrivateType.App;
using PrivateType.Core;

internal static class SettingsSoundProbe
{
    public static void Run(string outputDirectory)
    {
        VerifyLayout(outputDirectory, 800, dense: false);
        VerifyLayout(outputDirectory, 520, dense: true);
        VerifyDialogChoice(save: false);
        VerifyDialogChoice(save: true);
        VerifyCustomFileFlow(outputDirectory);
        if (AudioProbeEnabled)
            foreach (var sound in new[] { "ping", "chime", "bell" })
                VerifyAudiblePreview(PortableSettings.Default with { ReadySound = sound, ReadySoundVolume = 80 });
        Console.WriteLine("PASS: Settings ready-sound choices, silent preview, missing-file error, scroll, fixed actions, keyboard access, and save/cancel transitions.");
    }

    private static void VerifyLayout(string outputDirectory, double height, bool dense)
    {
        var settings = PortableSettings.Default with
        {
            Shortcuts = dense
                ? Enumerable.Range(0x70, 12).Select(key => new ShortcutBinding(RecognitionLanguage.English, key)).ToArray()
                : ShortcutBinding.Defaults
        };
        var window = CreateWindow(settings);
        window.SizeToContent = SizeToContent.Manual;
        window.Width = 620;
        window.Height = height;
        window.Show();
        try
        {
            Flush(window);
            var scroll = Find<ScrollViewer>(window, "SettingsScrollViewer");
            var save = Find<Button>(window, "SaveSettingsButton");
            var cancel = Find<Button>(window, "CancelSettingsButton");
            VerifyContained(window, save);
            VerifyContained(window, cancel);
            var saveBeforeScroll = save.TranslatePoint(new Point(), window);
            Require(scroll.ScrollableWidth < 1, "Settings must not overflow horizontally.");
            if (dense)
                Require(scroll.ScrollableHeight > 0 && scroll.ComputedVerticalScrollBarVisibility == Visibility.Visible,
                    "Dense settings require a visible vertical scrollbar.");
            Capture(window, outputDirectory, $"settings-sound-{height}-top.png");

            var sound = Find<ComboBox>(window, "ReadySoundBox");
            sound.BringIntoView();
            Flush(window);
            Require(sound.Items.Count == 4, "The sound selector must offer Ping, Chime, Bell, and custom sound.");
            foreach (var key in new[] { "ping", "chime", "bell", "custom" })
            {
                sound.SelectedValue = key;
                Flush(window);
                Require(Equals(sound.SelectedValue, key), $"Sound choice {key} is unavailable.");
                VerifySelectedLabel(sound);
            }
            Require(Find<TextBox>(window, "CustomSoundPathText").IsReadOnly, "The custom file display must be read-only.");
            foreach (var name in new[] { "ReadySoundBox", "ReadyVolumeSlider", "BrowseSoundButton", "PreviewSoundButton", "SaveSettingsButton", "CancelSettingsButton" })
                VerifyAccessibleFocus(window, name);

            var volume = Find<Slider>(window, "ReadyVolumeSlider");
            Require(volume.Minimum == 0 && volume.Maximum == 100, "Sound volume must cover 0–100%.");
            VerifyVolumeInteraction(window, volume);
            volume.Value = 100;
            Flush(window);
            Require(Find<TextBlock>(window, "ReadyVolumeText").Text.Contains("100"), "The visible volume must track the slider.");
            sound.BringIntoView();
            Flush(window);
            Capture(window, outputDirectory, $"settings-sound-{height}-custom.png");

            // No selected file means this preview must report validation before opening audio.
            Click(window, "PreviewSoundButton");
            PumpUntil(window, () => !string.IsNullOrWhiteSpace(Find<TextBlock>(window, "SoundStatusText").Text));
            Find<TextBlock>(window, "SoundStatusText").BringIntoView();
            Flush(window);
            Capture(window, outputDirectory, $"settings-sound-{height}-missing-file.png");
            Require(window.SavedSettings is null, "Preview must not save settings.");

            sound.SelectedValue = "ping";
            volume.Value = 0;
            Flush(window);
            Click(window, "PreviewSoundButton");
            PumpUntil(window, () => Find<Button>(window, "PreviewSoundButton").IsEnabled);
            Require(volume.Value == 0 && window.SavedSettings is null, "Muted preview must leave pending settings intact.");
            volume.BringIntoView();
            volume.Focus();
            Flush(window);
            Capture(window, outputDirectory, $"settings-sound-{height}-muted-focused.png");

            sound.BringIntoView();
            sound.IsDropDownOpen = true;
            Flush(window);
            var popup = sound.Template.FindName("Popup", sound) as Popup
                ?? sound.Template.FindName("PART_Popup", sound) as Popup;
            Require(popup?.IsOpen == true && popup.Child is FrameworkElement, "Sound choices must open a rendered dropdown.");
            Capture((FrameworkElement)popup!.Child, outputDirectory, $"settings-sound-{height}-choices.png");
            sound.IsDropDownOpen = false;

            scroll.ScrollToBottom();
            Flush(window);
            VerifyContained(window, save);
            Require((save.TranslatePoint(new Point(), window) - saveBeforeScroll).Length < 1, "Save actions must stay fixed while settings scroll.");
            Capture(window, outputDirectory, $"settings-sound-{height}-bottom.png");
        }
        finally
        {
            window.Close();
        }
    }

    private static SettingsWindow CreateWindow(PortableSettings settings) => new(settings,
        [new MicrophoneOption("default", "System default microphone — USB conference microphone with a deliberately long device name")])
    {
        ShowInTaskbar = false
    };

    private static void VerifySelectedLabel(ComboBox sound)
    {
        var selected = (ReadySoundOption)sound.SelectedItem;
        var visibleText = VisualDescendants(sound).OfType<TextBlock>().Where(text => text.IsVisible).Select(text => text.Text).ToArray();
        Require(visibleText.Contains(selected.Label), "The sound selector must visibly render its friendly label.");
        Require(!visibleText.Any(text => text.Contains("ReadySoundOption", StringComparison.Ordinal)),
            "The sound selector must not display record implementation text.");
    }

    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in VisualDescendants(child))
                yield return descendant;
        }
    }

    private static void VerifyVolumeInteraction(Window window, Slider volume)
    {
        volume.BringIntoView();
        Flush(window);
        var range = (IRangeValueProvider)new SliderAutomationPeer(volume).GetPattern(PatternInterface.RangeValue);
        Require(!range.IsReadOnly, "The accessible volume slider must be editable.");
        range.SetValue(40);
        Flush(window);
        Require(volume.Value == 40 && Find<TextBlock>(window, "ReadyVolumeText").Text == "40%",
            "Accessible volume changes must update the slider and visible percentage.");
        Slider.IncreaseSmall.Execute(null, volume);
        Flush(window);
        Require(volume.Value > 40, "The volume slider must respond to its keyboard increase command.");
        Slider.DecreaseSmall.Execute(null, volume);
        Flush(window);
        Require(volume.Value == 40, "The volume slider must respond to its keyboard decrease command.");
        range.SetValue(0);
        Flush(window);
        Require(Find<TextBlock>(window, "ReadyVolumeText").Text == "0%", "The accessible mute value must be reflected visibly.");
    }

    private static void VerifyCustomFileFlow(string outputDirectory)
    {
        var fixtureDirectory = Path.Combine(Path.GetTempPath(), $"privatetype-sound-ui-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fixtureDirectory);
        try
        {
            var soundPath = Path.Combine(fixtureDirectory, "A deliberately long custom ready sound filename for checking the settings field and retained original name.wav");
            WriteSilentWave(soundPath);
            if (AudioProbeEnabled)
            {
                VerifyAudiblePreview(PortableSettings.Default with { ReadySound = "custom", ReadySoundVolume = 80, CustomReadySoundPath = soundPath });
                VerifyMp3(soundPath, fixtureDirectory);
            }
            var invalidPath = Path.Combine(fixtureDirectory, "unreadable-audio.wav");
            File.WriteAllText(invalidPath, "Synthetic invalid audio fixture.");
            var dataDirectory = Path.Combine(fixtureDirectory, "app-data");
            VerifyCustomDialog(soundPath, invalidPath, outputDirectory, save: false);
            Require(File.Exists(soundPath) && !Directory.Exists(dataDirectory),
                "Cancelling a file selection must retain the original file without creating an app copy.");
            var selected = VerifyCustomDialog(soundPath, invalidPath, outputDirectory, save: true)
                ?? throw new InvalidOperationException("Custom sound save did not return preferences.");
            Require(selected.ReadySound == "custom" && selected.CustomReadySoundPath == soundPath && selected.ReadySoundVolume == 23,
                "Save must return the chosen custom file and volume.");
            var importedPath = ReadySoundStorage.Import(selected.CustomReadySoundPath!, dataDirectory);
            Require(importedPath != soundPath && File.Exists(importedPath), "Saving a custom sound must create the managed audio copy.");
            var store = new PortableSettingsStore(dataDirectory);
            store.Save(selected with { CustomReadySoundPath = importedPath });
            // Moving the original verifies the saved selection uses the managed copy.
            File.Move(soundPath, Path.Combine(fixtureDirectory, "moved-original.wav"));
            var loaded = store.Load();
            Require(loaded.Warning is null && loaded.Settings.CustomReadySoundPath == importedPath,
                "Custom sound preferences must survive persistence and moving the original.");
            ReadySoundStorage.Validate(loaded.Settings.CustomReadySoundPath!);
            var reopened = CreateWindow(loaded.Settings);
            reopened.Show();
            try
            {
                var selector = Find<ComboBox>(reopened, "ReadySoundBox");
                selector.BringIntoView();
                Flush(reopened);
                Require(Equals(selector.SelectedValue, "custom")
                    && Find<TextBox>(reopened, "CustomSoundPathText").Text == Path.GetFileName(soundPath)
                    && Find<Slider>(reopened, "ReadyVolumeSlider").Value == 23,
                    "Reopened settings must display the persisted custom filename and volume.");
                Capture(reopened, outputDirectory, "settings-sound-custom-persisted.png");
            }
            finally
            {
                reopened.Close();
            }
        }
        finally
        {
            Directory.Delete(fixtureDirectory, recursive: true);
        }
        Console.WriteLine("PASS: Custom file validation, muted preview, cancellation, import, persistence, and original-file relocation use synthetic temporary data only.");
    }

    private static void WriteSilentWave(string path)
    {
        const int sampleRate = 44100;
        const int dataLength = sampleRate / 10 * sizeof(short);
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataLength);
        writer.Write(new byte[dataLength]);
    }

    private static PortableSettings? VerifyCustomDialog(string soundPath, string invalidPath, string outputDirectory, bool save)
    {
        var window = CreateWindow(PortableSettings.Default);
        Exception? failure = null;
        window.ContentRendered += (_, _) => window.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                var selector = Find<ComboBox>(window, "ReadySoundBox");
                selector.SelectedValue = "custom";
                selector.BringIntoView();
                Require(window.SelectCustomSound(soundPath), "The synthetic WAV must be accepted by the real file-selection path.");
                Require(Find<TextBox>(window, "CustomSoundPathText").Text == Path.GetFileName(soundPath),
                    "Choosing a file must immediately display its filename.");
                Flush(window);
                Capture(window, outputDirectory, $"settings-sound-custom-{(save ? "save" : "cancel")}-selected.png");
                Require(!window.SelectCustomSound(invalidPath), "An invalid WAV must be rejected.");
                Require(Find<TextBox>(window, "CustomSoundPathText").Text == Path.GetFileName(soundPath)
                    && !string.IsNullOrWhiteSpace(Find<TextBlock>(window, "SoundStatusText").Text),
                    "An invalid replacement must show an error and retain the previous valid selection.");
                Flush(window);
                Find<TextBlock>(window, "SoundStatusText").BringIntoView();
                Flush(window);
                Capture(window, outputDirectory, "settings-sound-custom-invalid-replacement.png");
                var volume = Find<Slider>(window, "ReadyVolumeSlider");
                volume.Value = 0;
                Click(window, "PreviewSoundButton");
                Require(Find<TextBlock>(window, "SoundStatusText").Text.Contains("muted", StringComparison.OrdinalIgnoreCase),
                    "Muted custom preview must report its silent outcome without opening audio hardware.");
                volume.Value = 23;
                Click(window, save ? "SaveSettingsButton" : "CancelSettingsButton");
                Require(!window.IsVisible, "The custom sound dialog must close after Save or Cancel.");
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
        Require(result == save, "The custom sound dialog must report its chosen action.");
        Require(save || window.SavedSettings is null, "Cancel must discard the pending custom sound.");
        return window.SavedSettings;
    }

    private static bool AudioProbeEnabled => Environment.GetEnvironmentVariable("PRIVATE_TYPE_PROBE_AUDIO") == "1";

    private static void VerifyMp3(string wavePath, string fixtureDirectory)
    {
        var mp3Path = Path.Combine(fixtureDirectory, "synthetic-preview.mp3");
        using (var source = new NAudio.Wave.WaveFileReader(wavePath))
            NAudio.Wave.MediaFoundationEncoder.EncodeToMp3(source, mp3Path, 128000);
        ReadySoundStorage.Validate(mp3Path);
        var stored = ReadySoundStorage.Import(mp3Path, Path.Combine(fixtureDirectory, "mp3-data"));
        var settings = PortableSettings.Default with { ReadySound = "custom", CustomReadySoundPath = stored };
        Require(ReadySoundAudio.Load(settings, fallbackToPing: false).Samples.Length > 0,
            "The synthetic MP3 must decode after import without a fallback.");
        VerifyAudiblePreview(settings);
        Console.WriteLine("PASS: Synthetic MP3 encoded, decoded, imported, and previewed without fallback.");
    }

    private static void VerifyAudiblePreview(PortableSettings settings)
    {
        var window = CreateWindow(settings);
        window.Show();
        try
        {
            Flush(window);
            Click(window, "PreviewSoundButton");
            var finish = DateTime.UtcNow.AddSeconds(1.2);
            PumpUntil(window, () => DateTime.UtcNow >= finish);
            Require(Find<TextBlock>(window, "SoundStatusText").Text.StartsWith("Preview started", StringComparison.Ordinal),
                $"The opt-in {settings.ReadySound} preview must open the audio output successfully.");
            Console.WriteLine($"PASS: Opt-in audio output opened for {settings.ReadySound} preview at 80%.");
        }
        finally
        {
            window.Close();
        }
    }

    private static T Find<T>(Window window, string name) where T : FrameworkElement =>
        window.FindName(name) as T ?? throw new InvalidOperationException($"Missing settings control {name}.");

    private static void VerifyContained(Window window, FrameworkElement control)
    {
        var origin = control.TranslatePoint(new Point(), window);
        Require(control.IsVisible && control.ActualWidth > 0 && control.ActualHeight > 0
            && origin.X >= 0 && origin.Y >= 0
            && origin.X + control.ActualWidth <= window.ActualWidth + 1
            && origin.Y + control.ActualHeight <= window.ActualHeight + 1,
            $"{control.Name} must remain visible inside the window.");
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

    private static void VerifyAccessibleFocus(SettingsWindow window, string name)
    {
        var control = Find<Control>(window, name);
        control.BringIntoView();
        Flush(window);
        var peer = UIElementAutomationPeer.CreatePeerForElement(control);
        Require(!string.IsNullOrWhiteSpace(peer?.GetName()), $"{name} needs an accessible name.");
        Require(control.Focus() && control.IsKeyboardFocusWithin, $"{name} must accept keyboard focus.");
    }

    private static void Click(Window window, string name)
    {
        var button = Find<Button>(window, name);
        var peer = new ButtonAutomationPeer(button);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
        Flush(window);
    }

    private static void PumpUntil(Window window, Func<bool> complete)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        do
        {
            Flush(window);
            if (complete())
                return;
            Thread.Sleep(10);
        } while (DateTime.UtcNow < deadline);
        throw new InvalidOperationException("Settings preview did not finish within five seconds.");
    }

    private static void Flush(Window window)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    private static void VerifyDialogChoice(bool save)
    {
        var original = PortableSettings.Default;
        var window = CreateWindow(original);
        Exception? failure = null;
        window.ContentRendered += (_, _) => window.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                Find<ComboBox>(window, "ReadySoundBox").SelectedValue = "bell";
                Find<Slider>(window, "ReadyVolumeSlider").Value = 37;
                Click(window, save ? "SaveSettingsButton" : "CancelSettingsButton");
                if (window.IsVisible)
                    throw new InvalidOperationException("Save or Cancel did not close settings.");
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
        Require(result == save, "Settings dialog must report its selected action.");
        if (!save)
        {
            Require(window.SavedSettings is null, "Cancel must discard pending sound changes.");
            return;
        }
        var saved = window.SavedSettings;
        Require(saved is not null && saved.ReadySound == "bell" && saved.ReadySoundVolume == 37,
            "Save must return the selected sound and volume.");
        Require(saved!.MicrophoneId == original.MicrophoneId && saved.Shortcuts.SequenceEqual(original.Shortcuts)
            && saved.ModelIdleTimeoutMinutes == original.ModelIdleTimeoutMinutes,
            "Saving sound preferences must preserve existing settings.");
        var reopened = CreateWindow(saved);
        Require(Equals(Find<ComboBox>(reopened, "ReadySoundBox").SelectedValue, "bell")
            && Find<Slider>(reopened, "ReadyVolumeSlider").Value == 37,
            "Reopened settings must show saved sound preferences.");
        reopened.Close();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
