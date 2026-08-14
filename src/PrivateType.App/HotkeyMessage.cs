namespace PrivateType.App;

internal static class HotkeyMessage
{
    public const int KeyDown = 0x0100;
    public const int KeyUp = 0x0101;
    public const int SystemKeyDown = 0x0104;
    public const int SystemKeyUp = 0x0105;

    public static bool IsKeyDown(nint message)
    {
        return message is KeyDown or SystemKeyDown;
    }

    public static bool IsKeyUp(nint message)
    {
        return message is KeyUp or SystemKeyUp;
    }
}
