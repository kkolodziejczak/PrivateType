using System.Runtime.InteropServices;
using PrivateType.Core;

namespace PrivateType.App;

internal sealed record HotkeyDefinition(int Id, RecognitionLanguage Language, int VirtualKey, uint Modifiers, string Label);

internal static class HotkeyCatalog
{
    internal const uint ModifierControl = 0x0002;
    internal const uint ModifierShift = 0x0004;
    private const uint ModifierNoRepeat = 0x4000;

    internal static readonly HotkeyDefinition Polish = new(1, RecognitionLanguage.Polish, 0x52, ModifierControl | ModifierShift, "Ctrl+Shift+R");
    internal static readonly HotkeyDefinition English = new(2, RecognitionLanguage.English, 0x45, ModifierControl | ModifierShift, "Ctrl+Shift+E");
    internal static readonly IReadOnlyList<HotkeyDefinition> All = [Polish, English];

    internal static IReadOnlyList<HotkeyDefinition> FromBindings(IReadOnlyList<ShortcutBinding> bindings)
    {
        var validationError = PortableSettingsValidator.Validate(new PortableSettings("configured", bindings));
        if (validationError is not null)
            throw new ArgumentException(validationError, nameof(bindings));

        return bindings.Select((binding, index) => new HotkeyDefinition(
            index + 1,
            binding.Language,
            binding.VirtualKey,
            ModifierControl | ModifierShift,
            $"Ctrl+Shift+{KeyLabel(binding.VirtualKey)}")).ToArray();
    }

    internal static uint ToRegistrationModifiers(HotkeyDefinition hotkey)
    {
        return hotkey.Modifiers | ModifierNoRepeat;
    }

    private static string KeyLabel(int virtualKey)
    {
        return virtualKey switch
        {
            >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),
            >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),
            >= 0x70 and <= 0x87 => $"F{virtualKey - 0x6F}",
            _ => $"VK-{virtualKey:X2}"
        };
    }
}

internal sealed record HotkeyRegistrationAttempt(HotkeyDefinition Hotkey, bool Succeeded, int ErrorCode);

internal sealed class HotkeyAvailability
{
    private HotkeyAvailability(IReadOnlyList<HotkeyRegistrationAttempt> attempts)
    {
        Attempts = attempts;
        EnabledHotkeys = attempts.Where(attempt => attempt.Succeeded).Select(attempt => attempt.Hotkey).ToArray();
        DisabledHotkeys = attempts.Where(attempt => !attempt.Succeeded).Select(attempt => attempt.Hotkey).ToArray();
    }

    public IReadOnlyList<HotkeyRegistrationAttempt> Attempts { get; }
    public IReadOnlyList<HotkeyDefinition> EnabledHotkeys { get; }
    public IReadOnlyList<HotkeyDefinition> DisabledHotkeys { get; }
    public IReadOnlyList<RecognitionLanguage> EnabledLanguages => EnabledHotkeys.Select(hotkey => hotkey.Language).ToArray();
    public IReadOnlyList<RecognitionLanguage> DisabledLanguages => DisabledHotkeys.Select(hotkey => hotkey.Language).ToArray();
    public bool CanStart => EnabledHotkeys.Count > 0;

    public static HotkeyAvailability FromRegistrationResults(IReadOnlyList<HotkeyRegistrationAttempt> attempts)
    {
        return new HotkeyAvailability(attempts);
    }

    public string DescribeDisabledHotkeys()
    {
        return string.Join(", ", DisabledHotkeys.Select(hotkey => hotkey.Label));
    }
}

internal sealed class HotkeyReservation : IDisposable
{
    private readonly IReadOnlyList<HotkeyDefinition> reservedHotkeys;
    private bool disposed;

    private HotkeyReservation(HotkeyAvailability availability)
    {
        Availability = availability;
        reservedHotkeys = availability.EnabledHotkeys;
    }

    public HotkeyAvailability Availability { get; }

    public static HotkeyReservation Reserve(IReadOnlyList<HotkeyDefinition> hotkeys)
    {
        var attempts = hotkeys.Select(TryReserve).ToArray();
        return new HotkeyReservation(HotkeyAvailability.FromRegistrationResults(attempts));
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        foreach (var hotkey in reservedHotkeys)
            UnregisterHotKey(nint.Zero, hotkey.Id);
    }

    private static HotkeyRegistrationAttempt TryReserve(HotkeyDefinition hotkey)
    {
        var succeeded = RegisterHotKey(nint.Zero, hotkey.Id, HotkeyCatalog.ToRegistrationModifiers(hotkey), (uint)hotkey.VirtualKey);
        return new HotkeyRegistrationAttempt(hotkey, succeeded, succeeded ? 0 : Marshal.GetLastWin32Error());
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);
}
