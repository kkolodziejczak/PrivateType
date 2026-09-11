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
    [
        new(RecognitionLanguage.Polish, "Polish"),
        new(RecognitionLanguage.English, "English"),
        new(RecognitionLanguage.Auto, "Automatic")
    ];
    private readonly ObservableCollection<ShortcutBindingEditor> bindings;
    private readonly PortableSettings originalSettings;
    private readonly ModelReadySound soundPreview = new();
    private string? customSoundPath;

    public SettingsWindow(PortableSettings settings, IReadOnlyList<MicrophoneOption> microphones)
    {
        InitializeComponent();
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

        bindings.Add(new ShortcutBindingEditor(new ShortcutBinding(RecognitionLanguage.Polish, key), supportedLanguages));
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
            Shortcuts = bindings.Select(binding => new ShortcutBinding(binding.Language, binding.VirtualKey)).ToArray(),
            StartWithWindows = StartWithWindowsCheckBox.IsChecked == true,
            ModelIdleTimeoutMinutes = IdleTimeoutBox.SelectedValue is int minutes ? minutes : 10
        };
        var validationError = PortableSettingsValidator.Validate(settings);
        if (validationError is not null)
        {
            ValidationText.Text = validationError;
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

public sealed record LanguageOption(RecognitionLanguage Language, string Label);

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
    private RecognitionLanguage language;
    private int virtualKey;

    public ShortcutBindingEditor(ShortcutBinding binding, IReadOnlyList<LanguageOption> supportedLanguages)
    {
        language = binding.Language;
        virtualKey = binding.VirtualKey;
        SupportedLanguages = supportedLanguages;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<LanguageOption> SupportedLanguages { get; }

    public RecognitionLanguage Language
    {
        get => language;
        set
        {
            language = value;
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

    public string ShortcutLabel => HotkeyCatalog.FromBindings([new ShortcutBinding(Language, VirtualKey)]).Single().Label;

    private void Notify([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
