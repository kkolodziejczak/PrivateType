namespace PrivateType.App;

internal enum HeldKeyUpResult
{
    PassThrough,
    Released,
    Swallowed
}

// Tracks the one held dictation shortcut. Low-level hooks miss key-up events delivered to
// elevated windows, so the watchdog poll can release a shortcut whose key is physically up.
internal sealed class HeldShortcutTracker
{
    private int? recoveredVirtualKey;

    public HotkeyDefinition? Held { get; private set; }

    public bool TryPress(HotkeyDefinition hotkey)
    {
        if (Held is not null)
            return false;

        Held = hotkey;
        recoveredVirtualKey = null;
        return true;
    }

    public HeldKeyUpResult KeyUp(int virtualKey)
    {
        if (Held?.VirtualKey == virtualKey)
        {
            Held = null;
            return HeldKeyUpResult.Released;
        }

        // The key-down was swallowed, so also swallow a late key-up after a watchdog release.
        if (recoveredVirtualKey == virtualKey)
        {
            recoveredVirtualKey = null;
            return HeldKeyUpResult.Swallowed;
        }

        return HeldKeyUpResult.PassThrough;
    }

    public bool ReleaseIfPhysicallyUp(Func<int, bool> isPressed)
    {
        if (Held is null || isPressed(Held.VirtualKey))
            return false;

        recoveredVirtualKey = Held.VirtualKey;
        Held = null;
        return true;
    }

    public void Clear()
    {
        Held = null;
        recoveredVirtualKey = null;
    }
}
