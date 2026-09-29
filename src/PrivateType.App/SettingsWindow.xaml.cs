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
    private readonly ModelReadySound soundPreview = new();
    private readonly VocabularyEditor vocabulary;
    private string? customSoundPath;

    public SettingsWindow(PortableSettings settings, IReadOnlyList<MicrophoneOption> microphones, bool openVocabulary = false, string? vocabularyScope = null)
    {
        InitializeComponent();
        vocabulary = new VocabularyEditor(settings.Vocabulary, settings.VocabularyStrength, settings.VocabularyPacks, settings.VocabularyCorrections, settings.CorrectAfterDictation);
        if (VocabularyScopes.IsSupported(vocabularyScope))
            vocabulary.Scope = vocabularyScope!;
        VocabularyPage.DataContext = vocabulary;
        ChoosePackFile = PickPackFile;
        ConfirmPackRemoval = AskToRemovePack;
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
        IdleTimeoutBox.ItemsSource = IdleTimeoutOption.Supported;
        IdleTimeoutBox.SelectedValue = settings.ModelIdleTimeoutMinutes;
        customSoundPath = settings.CustomReadySoundPath;
        ReadySoundBox.ItemsSource = ReadySoundOption.Supported;
        ReadySoundBox.SelectedValue = settings.ReadySound;
        ReadyVolumeSlider.Value = settings.ReadySoundVolume;
        UpdateSoundControls();
        Closed += (_, _) => soundPreview.Dispose();
    }

    internal static string HeaderText(Version? version) => $"{ApplicationVersion.Label(version)} — settings";

    public PortableSettings? SavedSettings { get; private set; }
    public event Action? DiagnosticsRequested;
    public event Action? LicensesRequested;

    internal VocabularyEditor Vocabulary => vocabulary;

    internal void ShowVocabularyPage() => VocabularyTab.IsChecked = true;

    private void PageChanged(object sender, RoutedEventArgs e)
    {
        if (GeneralPage is null || VocabularyPage is null)
            return;

        var showVocabulary = VocabularyTab.IsChecked == true;
        GeneralPage.Visibility = showVocabulary ? Visibility.Collapsed : Visibility.Visible;
        VocabularyPage.Visibility = showVocabulary ? Visibility.Visible : Visibility.Collapsed;
        SettingsScrollViewer.ScrollToTop();
    }

    // Ctrl+Tab and Ctrl+Shift+Tab switch between the two pages.
    private void SwitchPageWithKeyboard(object sender, Input.KeyEventArgs e)
    {
        if (e.Key != Input.Key.Tab || (Input.Keyboard.Modifiers & Input.ModifierKeys.Control) == 0)
            return;

        var target = VocabularyTab.IsChecked == true ? GeneralTab : VocabularyTab;
        target.IsChecked = true;
        target.Focus();
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
            ? "Text arrives in one step, so pressing Enter right away cannot send half of it. Your clipboard is restored afterwards, and dictated text is kept out of clipboard history."
            : "Typing sends each character and never touches the clipboard. Use it where Ctrl+V does not paste, such as Vim or PuTTY.";
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

    private PortableSettings PendingSoundSettings() => originalSettings with
    {
        ReadySound = ReadySoundBox.SelectedValue as string ?? "ping",
        ReadySoundVolume = (int)Math.Round(ReadyVolumeSlider.Value),
        CustomReadySoundPath = customSoundPath
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
        var settings = PendingSoundSettings() with
        {
            MicrophoneId = MicrophoneBox.SelectedValue as string ?? "default",
            Shortcuts = bindings.Select(binding => new ShortcutBinding(binding.LocaleCode, binding.VirtualKey)).ToArray(),
            StartWithWindows = StartWithWindowsCheckBox.IsChecked == true,
            ShortcutMode = ShortcutModeBox.SelectedValue as string ?? DictationShortcutModes.Hold,
            InsertionMode = InsertionModeBox.SelectedValue as string ?? PortableSettings.Default.InsertionMode,
            Vocabulary = vocabulary.Entries,
            VocabularyPacks = vocabulary.Packs,
            VocabularyStrength = vocabulary.Strength,
            VocabularyCorrections = vocabulary.Corrections,
            CorrectAfterDictation = vocabulary.CorrectAfterDictation,
            ModelIdleTimeoutMinutes = IdleTimeoutBox.SelectedValue is int minutes ? minutes : 10
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
