using PrivateType.Core;

namespace PrivateType.App;

// The recent dictations shortcut (Win+Shift+V by default). Its key-down, auto-repeats, and key-up
// are all swallowed so the shortcut never reaches the focused app or the shell.
internal sealed class HistoryShortcut
{
    private KeyChord chord = KeyChord.DefaultHistory;
    private readonly ModifierReleaseMask releaseMask = new();
    private bool keyDown;

    public KeyChord Chord
    {
        get => chord;
        set
        {
            chord = value;
            Clear();
        }
    }

    // Returns true when the event belongs to the shortcut and must be swallowed. Opened is true
    // once per physical press.
    public bool Handle(nint message, int key, Func<int, bool> isPressed, out bool opened)
    {
        opened = false;
        if (key != chord.VirtualKey)
            return false;

        if (HotkeyMessage.IsKeyDown(message))
        {
            var combination = HeldModifiers.Read(isPressed, key) == chord;
            // An auto-repeat arrives while the combination is still held. Without it, the key-up
            // was missed (for example inside an elevated window), so this is an ordinary press.
            if (keyDown && combination)
                return true;
            keyDown = false;
            if (!combination)
                return false;

            keyDown = true;
            releaseMask.Arm(chord);
            opened = true;
            return true;
        }

        if (HotkeyMessage.IsKeyUp(message) && keyDown)
        {
            keyDown = false;
            return true;
        }

        return false;
    }

    public bool TakeModifierRelease(nint message, int key) => releaseMask.Take(message, key);

    public void Clear()
    {
        keyDown = false;
        releaseMask.Clear();
    }
}

// Reads which modifiers are held, as a chord without a key yet.
internal static class HeldModifiers
{
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLeftWindows = 0x5B;
    private const int VkRightWindows = 0x5C;

    public static KeyChord Read(Func<int, bool> isPressed, int virtualKey = 0) => new(
        Control: isPressed(VkControl),
        Shift: isPressed(VkShift),
        Alt: isPressed(VkMenu),
        Windows: isPressed(VkLeftWindows) || isPressed(VkRightWindows),
        VirtualKey: virtualKey);
}

// After a swallowed shortcut key, releasing Win alone opens Start and releasing Alt alone opens the
// focused app's menu bar, because the swallowed key does not count as a combination. The first such
// release is held back so the hook can replay it after an unassigned key.
internal sealed class ModifierReleaseMask
{
    private const int VkMenu = 0x12;
    private const int VkLeftWindows = 0x5B;
    private const int VkRightWindows = 0x5C;
    private const int VkLeftMenu = 0xA4;
    private const int VkRightMenu = 0xA5;
    private const ushort VkUnassigned = 0xE8;
    private bool windows;
    private bool alt;

    public void Arm(KeyChord chord)
    {
        windows = chord.Windows;
        alt = chord.Alt;
    }

    // True once for the first Win or Alt key-up after arming; that key-up must be swallowed and
    // replayed with Replay.
    public bool Take(nint message, int key)
    {
        var isWindows = key is VkLeftWindows or VkRightWindows;
        var isAlt = key is VkMenu or VkLeftMenu or VkRightMenu;
        if (!(windows && isWindows) && !(alt && isAlt))
            return false;
        // A fresh press means the earlier release was missed; never mask a later lone tap.
        var release = HotkeyMessage.IsKeyUp(message);
        Clear();
        return release;
    }

    public void Clear()
    {
        windows = false;
        alt = false;
    }

    // For a hook that is going away before the release arrives: an unassigned key pressed while Win
    // or Alt is still held makes the later release count as a combination.
    public void Neutralize()
    {
        if (!windows && !alt)
            return;
        Clear();
        NativeMethods.Input[] inputs =
        [
            new() { Type = NativeMethods.InputKeyboard, Data = new NativeMethods.InputUnion { Keyboard = new NativeMethods.KeybdInput { Vk = VkUnassigned } } },
            new() { Type = NativeMethods.InputKeyboard, Data = new NativeMethods.InputUnion { Keyboard = new NativeMethods.KeybdInput { Vk = VkUnassigned, Flags = NativeMethods.KeyEventKeyUp } } }
        ];
        NativeMethods.SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.Input>());
    }

    // An unassigned key followed by the held-back key-up, sent as one batch, keeps that order
    // however fast the keys were released.
    public static void Replay(ushort modifierKey)
    {
        NativeMethods.Input[] inputs =
        [
            new() { Type = NativeMethods.InputKeyboard, Data = new NativeMethods.InputUnion { Keyboard = new NativeMethods.KeybdInput { Vk = VkUnassigned } } },
            new() { Type = NativeMethods.InputKeyboard, Data = new NativeMethods.InputUnion { Keyboard = new NativeMethods.KeybdInput { Vk = VkUnassigned, Flags = NativeMethods.KeyEventKeyUp } } },
            new() { Type = NativeMethods.InputKeyboard, Data = new NativeMethods.InputUnion { Keyboard = new NativeMethods.KeybdInput { Vk = modifierKey, Flags = NativeMethods.KeyEventKeyUp } } }
        ];
        NativeMethods.SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.Input>());
    }
}
