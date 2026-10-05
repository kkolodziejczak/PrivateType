using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using PrivateType.Core;
using Input = System.Windows.Input;

namespace PrivateType.App;

public partial class SettingsWindow : Window
{
    private static readonly IReadOnlyList<LanguageOption> supportedLanguages =
        RecognitionLocaleCatalog.All.Select(locale => new LanguageOption(locale.Code, locale.DisplayName)).ToArray();
    private readonly ObservableCollection<ShortcutBindingEditor> bindings;
    private readonly PortableSettings originalSettings;
    private readonly DictationCues soundPreview = new();
    private readonly VocabularyEditor vocabulary;
    private readonly ModelLibraryEditor models;
    private string? customSoundPath;
    private KeyChord historyShortcut;
    private ShortcutRecorderHook? historyShortcutRecorder;

    internal SettingsWindow(PortableSettings settings, IReadOnlyList<MicrophoneOption> microphones, IModelStore modelStore, bool openVocabulary = false, string? vocabularyScope = null, string? notice = null, string? activeModelId = null)
    {
        InitializeComponent();
        if (notice is not null)
            ValidationText.Text = notice;
        models = new ModelLibraryEditor(modelStore, activeModelId ?? settings.SpeechModel, settings.SpeechModel);
        ModelPage.DataContext = models;
        OfflinePreviewCheckBox.IsChecked = settings.OfflinePreview;
        ConfirmModelDeletion = AskToDeleteModel;
        vocabulary = new VocabularyEditor(settings.Vocabulary, settings.VocabularyStrength, settings.VocabularyPacks, settings.VocabularyCorrections, settings.CorrectAfterDictation);
        if (VocabularyScopes.IsSupported(vocabularyScope))
            vocabulary.Scope = vocabularyScope!;
        VocabularyPage.DataContext = vocabulary;
        ChoosePackFile = PickPackFile;
        ConfirmPackRemoval = AskToRemovePack;
        ChooseCopyFolder = PickCopyFolder;
        ConfirmImport = copy => new ImportSettingsPromptWindow(copy) { Owner = this }.ShowDialog() == true;
        if (openVocabulary)
            VocabularyTab.IsChecked = true;
        MaxHeight = Math.Max(320, SystemParameters.WorkArea.Height - 24);
        Height = Math.Min(800, MaxHeight);
        var version = ApplicationVersion.Current;
        Title = $"{ApplicationVersion.Label(version)} settings";
        SettingsHeaderText.Text = HeaderText(version);
        originalSettings = settings;
        MicrophoneBox.ItemsSource = microphones;
        MicrophoneBox.SelectedValue = microphones.Any(microphone => microphone.Id == settings.MicrophoneId)
            ? settings.MicrophoneId
            : "default";
        bindings = new(settings.Shortcuts.Select(binding => new ShortcutBindingEditor(binding, supportedLanguages)));
        BindingsList.ItemsSource = bindings;
        StartWithWindowsCheckBox.IsChecked = settings.StartWithWindows;
        ShortcutModeBox.ItemsSource = ChoiceOption.ShortcutModes;
        ShortcutModeBox.SelectedValue = settings.ShortcutMode;
        InsertionModeBox.ItemsSource = ChoiceOption.InsertionModes;
        InsertionModeBox.SelectedValue = settings.InsertionMode;
        HistoryRetentionBox.ItemsSource = ChoiceOption.HistoryRetentions;
        HistoryRetentionBox.SelectedValue = settings.DictationHistory;
        ClipboardHistoryCheckBox.IsChecked = settings.IncludeInClipboardHistory;
        historyShortcut = settings.HistoryShortcut;
        HistoryShortcutBox.Text = historyShortcut.Label;
        UpdateHistoryHints();
        IdleTimeoutBox.ItemsSource = IdleTimeoutOption.Supported;
        IdleTimeoutBox.SelectedValue = settings.ModelIdleTimeoutMinutes;
        customSoundPath = settings.CustomReadySoundPath;
        ReadySoundBox.ItemsSource = ReadySoundOption.Supported;
        ReadySoundBox.SelectedValue = settings.ReadySound;
        ReadyVolumeSlider.Value = settings.ReadySoundVolume;
        SpokenCueVoiceBox.ItemsSource = ChoiceOption.SpokenCueVoices;
        SpokenCueVoiceBox.SelectedValue = settings.SpokenCueVoice;
        UpdateSoundControls();
        // Previews start from their first syllable, as they do while dictating.
        try
        {
            soundPreview.KeepOutputAwake();
        }
        catch (Exception)
        {
        }
        // A recorder left running after the user switches apps would swallow their shortcuts.
        Deactivated += (_, _) => StopRecordingHistoryShortcut();
        Closed += (_, _) =>
        {
            models.CancelAll();
            soundPreview.Dispose();
            StopRecordingHistoryShortcut();
        };
    }

    internal static string HeaderText(Version? version) => $"{ApplicationVersion.Label(version)} — settings";

    public PortableSettings? SavedSettings { get; private set; }

    // Set when the user imports another copy's settings: Settings closes, and reopens showing them unsaved.
    internal PortableSettings? ImportedSettings { get; private set; }
    internal EarlierCopy? ImportedFrom { get; private set; }
    internal string? ImportWarning { get; private set; }
    internal Func<string?> ChooseCopyFolder { get; set; }
    internal Func<EarlierCopy, bool> ConfirmImport { get; set; }
    public event Action? DiagnosticsRequested;
    public event Action? LicensesRequested;

    internal VocabularyEditor Vocabulary => vocabulary;
    internal ModelLibraryEditor Models => models;

    internal void ShowVocabularyPage() => VocabularyTab.IsChecked = true;
    internal void ShowModelPage() => ModelTab.IsChecked = true;

    private void PageChanged(object sender, RoutedEventArgs e)
    {
        if (GeneralPage is null || VocabularyPage is null || ModelPage is null)
            return;

        GeneralPage.Visibility = GeneralTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        VocabularyPage.Visibility = VocabularyTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ModelPage.Visibility = ModelTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SettingsScrollViewer.ScrollToTop();
    }

    // Ctrl+Tab and Ctrl+Shift+Tab cycle through the pages.
    private void SwitchPageWithKeyboard(object sender, Input.KeyEventArgs e)
    {
        if (e.Key != Input.Key.Tab || (Input.Keyboard.Modifiers & Input.ModifierKeys.Control) == 0)
            return;

        System.Windows.Controls.RadioButton[] tabs = [GeneralTab, VocabularyTab, ModelTab];
        var current = Array.FindIndex(tabs, tab => tab.IsChecked == true);
        var step = (Input.Keyboard.Modifiers & Input.ModifierKeys.Shift) != 0 ? tabs.Length - 1 : 1;
        var target = tabs[(current + step) % tabs.Length];
        target.IsChecked = true;
        target.Focus();
        e.Handled = true;
    }

    // Test seam: the layout probe replaces the confirmation.
    internal Func<SpeechModelDefinition, bool> ConfirmModelDeletion { get; set; }

    private bool AskToDeleteModel(SpeechModelDefinition model)
        => System.Windows.MessageBox.Show(
            this,
            $"Delete {model.DisplayName} from this computer? You can download it again later.",
            "Delete speech model",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question) == MessageBoxResult.OK;

    private void DownloadModel(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;
        _ = models.DownloadAsync(ModelRow(sender));
    }

    private void CancelModelDownload(object sender, RoutedEventArgs e) => models.CancelDownload(ModelRow(sender));

    private void UseModel(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;
        models.Use(ModelRow(sender));
    }

    private async void DeleteModel(object sender, RoutedEventArgs e)
    {
        var row = ModelRow(sender);
        if (ConfirmModelDeletion(row.Model))
            await models.DeleteAsync(row);
    }

    private static ModelLibraryEditor.SpeechModelRow ModelRow(object sender) => (ModelLibraryEditor.SpeechModelRow)((FrameworkElement)sender).Tag;

    private void OpenModelTerms(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void AddVocabularyPhrase(object sender, RoutedEventArgs e)
    {
        var row = vocabulary.Add();
        ValidationText.Text = string.Empty;
        Dispatcher.BeginInvoke(() =>
        {
            if (VocabularyList.ItemContainerGenerator.ContainerFromItem(row) is FrameworkElement container)
            {
                container.BringIntoView();
                FindChild<System.Windows.Controls.TextBox>(container)?.Focus();
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void RemoveVocabularyPhrase(object sender, RoutedEventArgs e)
    {
        vocabulary.Remove((VocabularyPhraseEditor)((FrameworkElement)sender).Tag);
        ValidationText.Text = string.Empty;
    }

    private void AddCorrection(object sender, RoutedEventArgs e)
    {
        var row = vocabulary.AddCorrection();
        ValidationText.Text = string.Empty;
        Dispatcher.BeginInvoke(() =>
        {
            if (CorrectionList.ItemContainerGenerator.ContainerFromItem(row) is FrameworkElement container)
            {
                container.BringIntoView();
                FindChild<System.Windows.Controls.TextBox>(container)?.Focus();
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void RemoveCorrection(object sender, RoutedEventArgs e)
    {
        vocabulary.RemoveCorrection((VocabularyCorrectionEditor)((FrameworkElement)sender).Tag);
        ValidationText.Text = string.Empty;
    }

    // Test seams: the layout probe replaces the file picker and confirmation.
    internal Func<string?> ChoosePackFile { get; set; }
    internal Func<string, bool> ConfirmPackRemoval { get; set; }

    private string? PickPackFile()
    {
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import vocabulary pack",
            Filter = $"PrivateType vocabulary pack (*{VocabularyPackCodec.Extension})|*{VocabularyPackCodec.Extension}|JSON files (*.json)|*.json",
            CheckFileExists = true,
            Multiselect = false
        };
        return picker.ShowDialog(this) == true ? picker.FileName : null;
    }

    private void ImportFromCopy(object sender, RoutedEventArgs e)
    {
        if (ChooseCopyFolder() is not { } folder)
            return;
        var copy = EarlierCopyFinder.FromChosenFolder(folder);
        if (copy is null)
        {
            ValidationText.Text = "No PrivateType settings were found in that folder. Choose the folder you unpacked PrivateType into.";
            return;
        }
        if (string.Equals(copy.AppDirectory, Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)), StringComparison.OrdinalIgnoreCase))
        {
            ValidationText.Text = "That folder is this copy of PrivateType. Choose another copy.";
            return;
        }
        if (!ConfirmImport(copy))
            return;

        try
        {
            var imported = SettingsImport.Read(copy, originalSettings.StartWithWindows);
            ImportedSettings = imported.Settings;
            ImportWarning = imported.Warning;
        }
        catch (IOException)
        {
            ValidationText.Text = "That copy's settings could not be read. Nothing was changed.";
            return;
        }
        ImportedFrom = copy;
        DialogResult = false;
    }

    private string? PickCopyFolder()
    {
        var picker = new Microsoft.Win32.OpenFolderDialog { Title = "Choose another PrivateType copy" };
        return picker.ShowDialog(this) == true ? picker.FolderName : null;
    }

    private bool AskToRemovePack(string name) =>
        System.Windows.MessageBox.Show(this,
            $"Remove the vocabulary pack \"{name}\"?\n\nIt is removed when you save Settings. The file you imported it from is not changed.",
            "Remove vocabulary pack", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    private void ImportPack(object sender, RoutedEventArgs e)
    {
        if (ChoosePackFile() is not { } path)
            return;

        IReadOnlyList<string> phrases;
        try
        {
            phrases = VocabularyPackCodec.Read(path);
        }
        catch (VocabularyPackFormatException exception)
        {
            ValidationText.Text = $"That pack could not be imported. {exception.Message}";
            return;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ValidationText.Text = "That pack file could not be read.";
            return;
        }

        var dialog = new ImportPackWindow(System.IO.Path.GetFileName(path), phrases, vocabulary) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } pack)
        {
            vocabulary.AddPack(pack);
            ValidationText.Text = string.Empty;
        }
    }

    private void EditPack(object sender, RoutedEventArgs e)
    {
        var item = (VocabularyPackItem)((FrameworkElement)sender).Tag;
        var dialog = new PackEditorWindow(item, vocabulary) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } pack)
            vocabulary.ReplacePack(item, pack);
    }

    private void ExportPack(object sender, RoutedEventArgs e)
    {
        var item = (VocabularyPackItem)((FrameworkElement)sender).Tag;
        new ExportPhrasesWindow($"Export {item.Name}", item.Phrases, item.Name) { Owner = this }.ShowDialog();
    }

    private void ExportPersonalPhrases(object sender, RoutedEventArgs e)
    {
        var scopeName = VocabularyScopes.Get(vocabulary.Scope).DisplayName;
        new ExportPhrasesWindow($"Export {scopeName} phrases", vocabulary.VisiblePhrases, $"My {scopeName} phrases") { Owner = this }.ShowDialog();
    }

    private void RemovePack(object sender, RoutedEventArgs e)
    {
        var item = (VocabularyPackItem)((FrameworkElement)sender).Tag;
        if (ConfirmPackRemoval(item.Name))
            vocabulary.RemovePack(item);
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                return match;
            if (FindChild<T>(child) is { } nested)
                return nested;
        }
        return null;
    }

    private void InsertionModeChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        InsertionModeHint.Text = InsertionModeBox.SelectedValue as string == TextInsertionModes.Paste
            ? "Text arrives in one step, so pressing Enter right away cannot send half of it. Your clipboard is restored afterwards."
            : "Typing sends each character and never touches the clipboard. Use it where Ctrl+V does not paste, such as Vim or PuTTY.";
        UpdateHistoryHints();
    }

    private void HistoryOptionChanged(object sender, RoutedEventArgs e) => UpdateHistoryHints();

    private void UpdateHistoryHints()
    {
        // Called while InitializeComponent is still wiring events.
        if (HistoryRetentionBox is null || HistoryHint is null || HistoryShortcutBox is null || HistoryShortcutLabel is null || ClipboardHistoryHint is null || ClipboardHistoryCheckBox is null || InsertionModeBox is null)
            return;

        // Before the constructor reads the saved shortcut, show the default.
        var label = historyShortcut?.Label ?? KeyChord.DefaultHistory.Label;
        var kept = HistoryRetentionBox.SelectedValue as string != DictationHistoryRetentions.Off;
        HistoryShortcutBox.IsEnabled = kept;
        HistoryShortcutLabel.Opacity = kept ? 0.8 : 0.45;
        HistoryShortcutBox.Opacity = kept ? 1 : 0.45;
        HistoryHint.Text = !kept
            ? $"No dictations are kept, and {label} is left to Windows and other apps."
            : $"Press {label} to pick a recent dictation and paste it into the active window. Kept in memory only, never saved to disk.";

        var pasting = InsertionModeBox.SelectedValue as string == TextInsertionModes.Paste;
        ClipboardHistoryCheckBox.IsEnabled = pasting;
        ClipboardHistoryHint.Text = !pasting
            ? "Applies only when text is inserted by pasting."
            : ClipboardHistoryCheckBox.IsChecked == true
                ? "Windows keeps them in clipboard history until you clear it or restart, and clipboard tools can read them. Cloud clipboard still skips them."
                : "Off keeps dictated text out of clipboard history, cloud clipboard, and clipboard tools.";
    }

    private void StartRecordingHistoryShortcut(object sender, Input.KeyboardFocusChangedEventArgs e)
    {
        if (historyShortcutRecorder is not null)
            return;
        try
        {
            historyShortcutRecorder = new ShortcutRecorderHook(RecordHistoryShortcut);
            HistoryShortcutBox.Text = "Press the new shortcut…";
        }
        catch (Exception)
        {
            ValidationText.Text = "Couldn't listen for the new shortcut. Close Settings and try again.";
        }
    }

    private void StopRecordingHistoryShortcut(object sender, Input.KeyboardFocusChangedEventArgs e) => StopRecordingHistoryShortcut();

    private void StopRecordingHistoryShortcut()
    {
        historyShortcutRecorder?.Dispose();
        historyShortcutRecorder = null;
        HistoryShortcutBox.Text = historyShortcut.Label;
    }

    // Keeps the previous shortcut when the new one can't be used, and says why.
    internal void RecordHistoryShortcut(KeyChord chord)
    {
        var shortcuts = bindings.Select(binding => new ShortcutBinding(binding.LocaleCode, binding.VirtualKey)).ToArray();
        if (HistoryShortcutRules.Validate(chord, shortcuts) is { } error)
        {
            ValidationText.Text = error;
            HistoryShortcutBox.Text = historyShortcut.Label;
            return;
        }

        historyShortcut = chord;
        HistoryShortcutBox.Text = chord.Label;
        ValidationText.Text = string.Empty;
        UpdateHistoryHints();
    }

    private void ReadySoundChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (CustomSoundPathText is null)
            return;

        soundPreview.Stop();
        UpdateSoundControls();
    }

    private void UpdateSoundControls()
    {
        var custom = ReadySoundBox.SelectedValue as string == "custom";
        BrowseSoundButton.IsEnabled = custom;
        CustomSoundPathText.IsEnabled = custom;
        CustomSoundPathText.Text = string.IsNullOrWhiteSpace(customSoundPath)
            ? "Choose a WAV or MP3 file"
            : Path.GetFileName(customSoundPath);
        CustomSoundPathText.ToolTip = customSoundPath;
        SoundStatusText.Text = custom && string.IsNullOrWhiteSpace(customSoundPath)
            ? "Choose a file with Browse before previewing or saving."
            : string.Empty;
        ReadyVolumeText.Text = $"{ReadyVolumeSlider.Value:0}%";
    }

    private void ReadyVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ReadyVolumeText is null)
            return;

        soundPreview.Stop();
        ReadyVolumeText.Text = $"{e.NewValue:0}%";
        SoundStatusText.Text = string.Empty;
    }

    private void BrowseSound(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a ready sound",
            Filter = "Audio files (*.wav;*.mp3)|*.wav;*.mp3",
            CheckFileExists = true,
            Multiselect = false
        };
        if (picker.ShowDialog(this) != true)
            return;

        SelectCustomSound(picker.FileName);
    }

    internal bool SelectCustomSound(string path)
    {
        try
        {
            ReadySoundStorage.Validate(path);
            soundPreview.Stop();
            customSoundPath = path;
            UpdateSoundControls();
            return true;
        }
        catch (Exception)
        {
            SoundStatusText.Text = "Couldn't read that sound. Choose a valid WAV or MP3 file.";
            SoundStatusText.BringIntoView();
            return false;
        }
    }

    private void PreviewSound(object sender, RoutedEventArgs e)
    {
        try
        {
            soundPreview.Preview(PendingSoundSettings());
            SoundStatusText.Text = ReadyVolumeSlider.Value == 0
                ? "Sound is muted. Increase the volume to hear a preview."
                : "Preview started. Playback lasts up to 3 seconds.";
        }
        catch (Exception)
        {
            SoundStatusText.Text = "Couldn't play the sound. Check your audio output or choose a valid WAV or MP3 file.";
        }
        SoundStatusText.BringIntoView();
    }

    private void SpokenCueVoiceChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (PreviewSpokenCueButton is null)
            return;

        soundPreview.Stop();
        PreviewSpokenCueButton.IsEnabled = SpokenCueVoiceBox.SelectedValue as string != SpokenCueVoices.Off;
    }

    private void PreviewSpokenCue(object sender, RoutedEventArgs e)
    {
        try
        {
            soundPreview.Announce(SpokenCue.LoadingModel, PendingSoundSettings());
            SoundStatusText.Text = ReadyVolumeSlider.Value == 0
                ? "Sound is muted. Increase the volume to hear a preview."
                : "Preview started.";
        }
        catch (Exception)
        {
            SoundStatusText.Text = "Couldn't play the spoken cue. Check your audio output.";
        }
        SoundStatusText.BringIntoView();
    }

    private PortableSettings PendingSoundSettings() => originalSettings with
    {
        ReadySound = ReadySoundBox.SelectedValue as string ?? "ping",
        ReadySoundVolume = (int)Math.Round(ReadyVolumeSlider.Value),
        CustomReadySoundPath = customSoundPath,
        SpokenCueVoice = SpokenCueVoiceBox.SelectedValue as string ?? PortableSettings.Default.SpokenCueVoice
    };

    private void AddBinding(object sender, RoutedEventArgs e)
    {
        var key = Enumerable.Range(0x70, 24).FirstOrDefault(candidate => bindings.All(binding => binding.VirtualKey != candidate));
        if (key == 0)
        {
            ValidationText.Text = "No unused function-key shortcut is available.";
            return;
        }

        bindings.Add(new ShortcutBindingEditor(new ShortcutBinding(RecognitionLocaleCatalog.Polish, key), supportedLanguages));
        ValidationText.Text = string.Empty;
    }

    private void RemoveBinding(object sender, RoutedEventArgs e)
    {
        if (bindings.Count == 1)
        {
            ValidationText.Text = "At least one shortcut is required.";
            return;
        }

        bindings.Remove((ShortcutBindingEditor)((FrameworkElement)sender).Tag);
        ValidationText.Text = string.Empty;
    }

    private void RecordShortcut(object sender, Input.KeyEventArgs e)
    {
        var key = e.Key == Input.Key.System ? e.SystemKey : e.Key;
        if ((Input.Keyboard.Modifiers & (Input.ModifierKeys.Control | Input.ModifierKeys.Shift)) != (Input.ModifierKeys.Control | Input.ModifierKeys.Shift)
            || key is Input.Key.LeftCtrl or Input.Key.RightCtrl or Input.Key.LeftShift or Input.Key.RightShift or Input.Key.System)
        {
            ValidationText.Text = "Use Ctrl+Shift plus a letter, number, or function key.";
            e.Handled = true;
            return;
        }

        var editor = (ShortcutBindingEditor)((FrameworkElement)sender).DataContext;
        var virtualKey = Input.KeyInterop.VirtualKeyFromKey(key);
        if (bindings.Any(binding => binding != editor && binding.VirtualKey == virtualKey))
        {
            ValidationText.Text = "Each shortcut must use a different key.";
            e.Handled = true;
            return;
        }

        editor.VirtualKey = virtualKey;
        ValidationText.Text = string.Empty;
        e.Handled = true;
    }

    private void Save(object sender, RoutedEventArgs e)
    {
        if (models.IsBusy)
        {
            ValidationText.Text = "Wait for the model download or deletion to finish before saving.";
            ModelTab.IsChecked = true;
            return;
        }

        var settings = PendingSoundSettings() with
        {
            MicrophoneId = MicrophoneBox.SelectedValue as string ?? "default",
            Shortcuts = bindings.Select(binding => new ShortcutBinding(binding.LocaleCode, binding.VirtualKey)).ToArray(),
            StartWithWindows = StartWithWindowsCheckBox.IsChecked == true,
            ShortcutMode = ShortcutModeBox.SelectedValue as string ?? DictationShortcutModes.Hold,
            InsertionMode = InsertionModeBox.SelectedValue as string ?? PortableSettings.Default.InsertionMode,
            DictationHistory = HistoryRetentionBox.SelectedValue as string ?? PortableSettings.Default.DictationHistory,
            IncludeInClipboardHistory = ClipboardHistoryCheckBox.IsChecked == true,
            HistoryShortcut = historyShortcut,
            Vocabulary = vocabulary.Entries,
            VocabularyPacks = vocabulary.Packs,
            VocabularyStrength = vocabulary.Strength,
            VocabularyCorrections = vocabulary.Corrections,
            CorrectAfterDictation = vocabulary.CorrectAfterDictation,
            ModelIdleTimeoutMinutes = IdleTimeoutBox.SelectedValue is int minutes ? minutes : 10,
            SpeechModel = models.SelectedId,
            OfflinePreview = OfflinePreviewCheckBox.IsChecked == true
        };
        var validationError = PortableSettingsValidator.Validate(settings);
        if (validationError is not null)
        {
            ValidationText.Text = validationError;
            if (VocabularyRules.Validate(settings.Vocabulary, settings.VocabularyPacks) is not null
                || VocabularyCorrectionRules.Validate(settings.VocabularyCorrections) is not null)
                VocabularyTab.IsChecked = true;
            return;
        }

        if (settings.ReadySound == "custom" &&
            (settings.CustomReadySoundPath != originalSettings.CustomReadySoundPath || originalSettings.ReadySound != "custom"))
        {
            try
            {
                ReadySoundStorage.Validate(settings.CustomReadySoundPath!);
            }
            catch (Exception)
            {
                ValidationText.Text = "Choose a readable WAV or MP3 file for the custom ready sound.";
                return;
            }
        }

        SavedSettings = settings;
        DialogResult = true;
    }

    private void CloseWindow(object sender, RoutedEventArgs e) => Close();

    private void OpenDiagnostics(object sender, RoutedEventArgs e) => DiagnosticsRequested?.Invoke();
    private void OpenLicenses(object sender, RoutedEventArgs e) => LicensesRequested?.Invoke();

    private void DragWindow(object sender, Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == Input.MouseButtonState.Pressed)
            DragMove();
    }
}

public sealed record LanguageOption(string LocaleCode, string Label);

public sealed record ChoiceOption(string Id, string Label)
{
    public static IReadOnlyList<ChoiceOption> ShortcutModes { get; } =
    [
        new(DictationShortcutModes.Hold, "Hold to talk"),
        new(DictationShortcutModes.Toggle, "Press to start and stop")
    ];

    public static IReadOnlyList<ChoiceOption> InsertionModes { get; } =
    [
        new(TextInsertionModes.Paste, "Pasting"),
        new(TextInsertionModes.Type, "Typing characters")
    ];

    public static IReadOnlyList<ChoiceOption> SpokenCueVoices { get; } =
    [
        new(PrivateType.Core.SpokenCueVoices.Female, "Female voice"),
        new(PrivateType.Core.SpokenCueVoices.Male, "Male voice"),
        new(PrivateType.Core.SpokenCueVoices.Off, "Off")
    ];

    public static IReadOnlyList<ChoiceOption> HistoryRetentions { get; } =
    [
        new(DictationHistoryRetentions.UntilExit, "Until PrivateType exits"),
        new(DictationHistoryRetentions.OneHour, "For 1 hour"),
        new(DictationHistoryRetentions.FifteenMinutes, "For 15 minutes"),
        new(DictationHistoryRetentions.Off, "Don't keep")
    ];
}

public sealed record ReadySoundOption(string Id, string Label)
{
    public static IReadOnlyList<ReadySoundOption> Supported { get; } =
    [
        new("ping", "Ping"),
        new("chime", "Chime"),
        new("bell", "Bell"),
        new("custom", "Custom file")
    ];
}

public sealed record IdleTimeoutOption(int Minutes, string Label)
{
    public static IReadOnlyList<IdleTimeoutOption> Supported { get; } =
    [
        new(5, "5 minutes"),
        new(10, "10 minutes"),
        new(15, "15 minutes"),
        new(30, "30 minutes")
    ];
}

public sealed class ShortcutBindingEditor : INotifyPropertyChanged
{
    private string localeCode;
    private int virtualKey;

    public ShortcutBindingEditor(ShortcutBinding binding, IReadOnlyList<LanguageOption> supportedLanguages)
    {
        localeCode = binding.LocaleCode;
        virtualKey = binding.VirtualKey;
        SupportedLanguages = supportedLanguages;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<LanguageOption> SupportedLanguages { get; }

    public string LocaleCode
    {
        get => localeCode;
        set
        {
            localeCode = value;
            Notify();
        }
    }

    public int VirtualKey
    {
        get => virtualKey;
        set
        {
            virtualKey = value;
            Notify();
            Notify(nameof(ShortcutLabel));
        }
    }

    public string ShortcutLabel => HotkeyCatalog.FromBindings([new ShortcutBinding(LocaleCode, VirtualKey)]).Single().Label;

    private void Notify([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
