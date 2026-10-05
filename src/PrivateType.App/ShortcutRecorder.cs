using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using PrivateType.Core;

namespace PrivateType.App;

// Records a combination such as Win+Shift+V. Windows claims many Win combinations before a window
// sees them, so recording uses a keyboard hook that runs only while the shortcut box has focus.
internal sealed class ShortcutRecorderHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int LlkhfInjected = 0x10;
    private readonly KeyChordRecorder recorder = new();
    private readonly HookProcedure callback;
    private readonly Action<KeyChord> recorded;
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private nint hook;

    public ShortcutRecorderHook(Action<KeyChord> recorded)
    {
        this.recorded = recorded;
        callback = HookCallback;
        hook = SetWindowsHookEx(WhKeyboardLl, callback, GetModuleHandle(Process.GetCurrentProcess().MainModule?.ModuleName), 0);
        if (hook == nint.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not listen for the new shortcut.");
    }

    public void Dispose()
    {
        recorder.ReleaseModifiers();
        if (hook != nint.Zero)
            UnhookWindowsHookEx(hook);
        hook = nint.Zero;
    }

    private nint HookCallback(int code, nint wParam, nint lParam)
    {
        if (code < 0)
            return CallNextHookEx(hook, code, wParam, lParam);
        var key = Marshal.ReadInt32(lParam);
        // The replayed release below arrives as injected input while Win or Alt still reads as held;
        // recording it would swallow the replay and open Start or the menu bar after all.
        if ((Marshal.ReadInt32(lParam, 8) & LlkhfInjected) != 0)
            return CallNextHookEx(hook, code, wParam, lParam);
        if (recorder.TakeModifierRelease(wParam, key))
        {
            ModifierReleaseMask.Replay((ushort)key);
            return 1;
        }

        if (recorder.Handle(wParam, key, IsPressed, out var chord))
        {
            // Leave the hook quickly; the window updates on its own turn.
            if (chord is not null)
                dispatcher.BeginInvoke(recorded, chord);
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

internal sealed class KeyChordRecorder
{
    private readonly ModifierReleaseMask releaseMask = new();
    private int? swallowedKey;

    // Returns true when the event must be swallowed. Recorded is set once per pressed combination.
    public bool Handle(nint message, int key, Func<int, bool> isPressed, out KeyChord? recorded)
    {
        recorded = null;
        if (HotkeyMessage.IsKeyUp(message) && key == swallowedKey)
        {
            swallowedKey = null;
            return true;
        }

        if (!HotkeyMessage.IsKeyDown(message) || IsModifier(key))
            return false;

        var chord = HeldModifiers.Read(isPressed, key);
        // Plain keys and Shift+key pass through, so Tab moves on and Esc closes Settings.
        if (!chord.Control && !chord.Alt && !chord.Windows)
            return false;
        if (swallowedKey == key)
            return true;

        swallowedKey = key;
        releaseMask.Arm(chord);
        recorded = chord;
        return true;
    }

    public bool TakeModifierRelease(nint message, int key) => releaseMask.Take(message, key);

    public void ReleaseModifiers() => releaseMask.Neutralize();

    private static bool IsModifier(int key) => key is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5);
}
