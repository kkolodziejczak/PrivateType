using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using PrivateType.Core;

namespace PrivateType.App;

internal sealed class HoldHotkeyHook : IDisposable
{
    internal const string NoAvailableShortcutMessage = "No dictation shortcut is available. Close another PrivateType copy that may be using the configured shortcuts, then try again.";

    private const int WhKeyboardLl = 13;
    private const int VkControl = 0x11;
    private const int VkShift = 0x10;
    private const ushort VkUnassigned = 0xE8;
    private readonly HookProcedure callback;
    private nint hook;
    private readonly HeldShortcutTracker held = new();
    private readonly HistoryShortcut history = new();
    private HotkeyReservation? reservation;
    private IReadOnlyList<HotkeyDefinition> configuredHotkeys = [];

    public HoldHotkeyHook() => callback = HookCallback;

    public event Action<string>? Held;
    public event Action? Released;
    public event Action? HistoryRequested;

    // Win+Shift+V is claimed only while dictation history is on; otherwise Windows keeps it.
    public bool HistoryShortcutEnabled
    {
        get => historyShortcutEnabled;
        set
        {
            historyShortcutEnabled = value;
            if (!value)
                history.Clear();
        }
    }

    private bool historyShortcutEnabled;

    // Hold: dictate while the shortcut is down. Toggle: press to start, press again to stop.
    public bool ToggleMode
    {
        get => toggleMode;
        set
        {
            if (toggleMode != value)
                held.Clear();
            toggleMode = value;
        }
    }

    private bool toggleMode;

    public HotkeyAvailability Start(IReadOnlyList<HotkeyDefinition> hotkeys)
    {
        reservation = HotkeyReservation.Reserve(hotkeys);
        if (!reservation.Availability.CanStart)
        {
            reservation.Dispose();
            reservation = null;
            throw new Win32Exception(NoAvailableShortcutMessage);
        }
        configuredHotkeys = hotkeys;

        hook = SetWindowsHookEx(WhKeyboardLl, callback, GetModuleHandle(Process.GetCurrentProcess().MainModule?.ModuleName), 0);
        if (hook == nint.Zero)
        {
            reservation.Dispose();
            reservation = null;
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not register the global dictation shortcut.");
        }

        return reservation.Availability;
    }

    public void Suspend()
    {
        reservation?.Dispose();
        reservation = null;
        held.Clear();
        history.Clear();
    }

    public bool ReleaseIfKeyIsUp()
    {
        if (!held.ReleaseIfPhysicallyUp(IsPressed))
            return false;

        Released?.Invoke();
        return true;
    }

    public HotkeyAvailability? Resume(IReadOnlyList<HotkeyDefinition> hotkeys)
    {
        // Replace an active reservation rather than failing, so a repeated resume cannot leave
        // the app without working shortcuts.
        reservation?.Dispose();
        reservation = null;

        var replacement = HotkeyReservation.Reserve(hotkeys);
        if (!replacement.Availability.CanStart)
        {
            replacement.Dispose();
            return null;
        }

        reservation = replacement;
        configuredHotkeys = hotkeys;
        return replacement.Availability;
    }

    public void Dispose()
    {
        if (hook != nint.Zero)
            UnhookWindowsHookEx(hook);
        hook = nint.Zero;
        reservation?.Dispose();
        reservation = null;
        configuredHotkeys = [];
        held.Clear();
    }

    private nint HookCallback(int code, nint wParam, nint lParam)
    {
        if (code < 0)
            return CallNextHookEx(hook, code, wParam, lParam);
        var key = Marshal.ReadInt32(lParam);
        // Suspended along with the dictation shortcuts while Settings or Teach is open.
        if (HistoryShortcutEnabled && reservation is not null && history.Handle(wParam, key, IsPressed, out var opened))
        {
            if (opened)
            {
                MaskWindowsKeyRelease();
                HistoryRequested?.Invoke();
            }
            return 1;
        }

        var hotkey = reservation?.Availability.EnabledHotkeys.SingleOrDefault(candidate => candidate.VirtualKey == key);

        if (ToggleMode)
            return HandleToggle(code, wParam, lParam, key, hotkey);

        if (HotkeyMessage.IsKeyDown(wParam) && held.Held is null && hotkey is not null && IsPressed(VkControl) && IsPressed(VkShift))
        {
            held.TryPress(hotkey);
            Held?.Invoke(hotkey.LocaleCode);
            return 1;
        }
        if (HotkeyMessage.IsKeyUp(wParam))
        {
            switch (held.KeyUp(key))
            {
                case HeldKeyUpResult.Released:
                    Released?.Invoke();
                    return 1;
                case HeldKeyUpResult.Swallowed:
                    return 1;
            }
        }
        return CallNextHookEx(hook, code, wParam, lParam);
    }

    private nint HandleToggle(int code, nint wParam, nint lParam, int key, HotkeyDefinition? hotkey)
    {
        if (HotkeyMessage.IsKeyDown(wParam) && hotkey is not null && IsPressed(VkControl) && IsPressed(VkShift))
        {
            switch (held.ToggleKeyDown(hotkey))
            {
                case ToggleKeyResult.Started:
                    Held?.Invoke(hotkey.LocaleCode);
                    break;
                case ToggleKeyResult.Stopped:
                    Released?.Invoke();
                    break;
            }
            return 1;
        }
        if (HotkeyMessage.IsKeyUp(wParam) && held.ToggleKeyUp(key))
            return 1;
        return CallNextHookEx(hook, code, wParam, lParam);
    }

    // The shell opens Start when Win is released without another key in between. The swallowed V
    // does not count, so send an unassigned key to mark Win as used in a combination.
    private static void MaskWindowsKeyRelease()
    {
        NativeMethods.Input[] inputs =
        [
            new() { Type = NativeMethods.InputKeyboard, Data = new NativeMethods.InputUnion { Keyboard = new NativeMethods.KeybdInput { Vk = VkUnassigned } } },
            new() { Type = NativeMethods.InputKeyboard, Data = new NativeMethods.InputUnion { Keyboard = new NativeMethods.KeybdInput { Vk = VkUnassigned, Flags = NativeMethods.KeyEventKeyUp } } }
        ];
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());
    }

    private static bool IsPressed(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private delegate nint HookProcedure(int code, nint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int idHook, HookProcedure procedure, nint module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? moduleName);
}
