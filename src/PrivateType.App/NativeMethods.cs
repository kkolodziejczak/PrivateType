using System.Runtime.InteropServices;

namespace PrivateType.App;

internal static class NativeMethods
{
    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    internal static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint window);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    internal static extern nint SetWindowLongPtr(nint window, int index, nint value);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint count, Input[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Input { public uint Type; public InputUnion Data; }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeybdInput Keyboard;
        [FieldOffset(0)] public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeybdInput { public ushort Vk; public ushort Scan; public uint Flags; public uint Time; public nint ExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput { public int X; public int Y; public uint MouseData; public uint Flags; public uint Time; public nint ExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HardwareInput { public uint Message; public ushort ParameterLow; public ushort ParameterHigh; }

    internal const uint InputKeyboard = 1;
    internal const uint KeyEventUnicode = 0x0004;
    internal const uint KeyEventKeyUp = 0x0002;
}
