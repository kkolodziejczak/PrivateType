namespace PrivateType.App;

// Win+Shift+V opens recent dictations. Its V key-down, auto-repeats, and key-up are all swallowed
// so the shortcut never reaches the focused app or the shell.
internal sealed class HistoryShortcut
{
    internal const string Label = "Win+Shift+V";
    internal const int VirtualKey = 0x56;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLeftWindows = 0x5B;
    private const int VkRightWindows = 0x5C;
    private bool keyDown;

    // Returns true when the event belongs to the shortcut and must be swallowed. Opened is true
    // once per physical press.
    public bool Handle(nint message, int key, Func<int, bool> isPressed, out bool opened)
    {
        opened = false;
        if (key != VirtualKey)
            return false;

        if (HotkeyMessage.IsKeyDown(message))
        {
            var combination = (isPressed(VkLeftWindows) || isPressed(VkRightWindows)) && isPressed(VkShift) && !isPressed(VkControl) && !isPressed(VkMenu);
            // An auto-repeat arrives while the combination is still held. Without it, the V key-up
            // was missed (for example inside an elevated window), so this is an ordinary V press.
            if (keyDown && combination)
                return true;
            keyDown = false;
            if (!combination)
                return false;

            keyDown = true;
            windowsReleasePending = true;
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

    // True once for the first Win key-up after the shortcut opened. The hook must then swallow that
    // key-up and replay it after an unassigned key, or the shell opens Start because the swallowed V
    // does not count as a combination.
    public bool TakeWindowsRelease(nint message, int key)
    {
        if (!windowsReleasePending || key is not (VkLeftWindows or VkRightWindows))
            return false;
        // A fresh Win press means the earlier release was missed; never mask a later lone Win tap.
        if (!HotkeyMessage.IsKeyUp(message))
        {
            windowsReleasePending = false;
            return false;
        }

        windowsReleasePending = false;
        return true;
    }

    private bool windowsReleasePending;

    public void Clear()
    {
        keyDown = false;
        windowsReleasePending = false;
    }
}
