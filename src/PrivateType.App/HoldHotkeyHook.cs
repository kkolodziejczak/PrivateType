using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using PrivateType.Core;

namespace PrivateType.App;

internal sealed class HoldHotkeyHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int VkControl = 0x11;
    private const int VkShift = 0x10;
    private readonly HookProcedure callback;
    private nint hook;
    private HotkeyReservation? reservation;
    private HotkeyDefinition? heldHotkey;
    private IReadOnlyList<HotkeyDefinition> configuredHotkeys = [];

    public HoldHotkeyHook() => callback = HookCallback;

    public event Action<RecognitionLanguage>? Held;
    public event Action? Released;

    public HotkeyAvailability Start(IReadOnlyList<HotkeyDefinition> hotkeys)
    {
        reservation = HotkeyReservation.Reserve(hotkeys);
        if (!reservation.Availability.CanStart)
        {
            reservation.Dispose();
            reservation = null;
            throw new Win32Exception("Nie można zarezerwować żadnego skrótu dyktowania.");
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
        heldHotkey = null;
    }

    public HotkeyAvailability? Resume(IReadOnlyList<HotkeyDefinition> hotkeys)
    {
        if (reservation is not null)
            throw new InvalidOperationException("Dictation hotkeys are already active.");

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
        heldHotkey = null;
    }

    private nint HookCallback(int code, nint wParam, nint lParam)
    {
        if (code < 0)
            return CallNextHookEx(hook, code, wParam, lParam);
        var key = Marshal.ReadInt32(lParam);
        var hotkey = reservation?.Availability.EnabledHotkeys.SingleOrDefault(candidate => candidate.VirtualKey == key);

        if (HotkeyMessage.IsKeyDown(wParam) && heldHotkey is null && hotkey is not null && IsPressed(VkControl) && IsPressed(VkShift))
        {
            heldHotkey = hotkey;
            Held?.Invoke(hotkey.Language);
            return 1;
        }
        if (HotkeyMessage.IsKeyUp(wParam) && heldHotkey?.VirtualKey == key)
        {
            heldHotkey = null;
            Released?.Invoke();
            return 1;
        }
        return CallNextHookEx(hook, code, wParam, lParam);
    }

    private static bool IsPressed(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private delegate nint HookProcedure(int code, nint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int idHook, HookProcedure procedure, nint module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? moduleName);
}
