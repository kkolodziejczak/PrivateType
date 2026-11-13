using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

// Runs a program on a Windows desktop of its own, which is never shown and gets no input. What the program draws
// there can't land on the screen, a person's pointer and keyboard can't reach it, and no other program's window can
// take the active window from it. On the shared desktop two captures drawn at once lost a hover, a focus ring or a
// window's active look in one run of five, and a capture drawn alone did once in nineteen.
internal static class HiddenDesktop
{
    public static int Run(string program, string arguments)
    {
        var name = $"{Prefix}{Guid.NewGuid():N}";
        var desktop = CreateDesktop(name, null, 0, 0, GenericAll, 0);
        if (desktop == 0)
            throw new Win32Exception();
        try
        {
            var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>(), Desktop = name };
            // Handles are inherited so the program writes to this console, and its failures reach the caller's log.
            if (!CreateProcess(program, new StringBuilder($"\"{program}\" {arguments}"), 0, 0, true, 0, 0, null, ref startup, out var started))
                throw new Win32Exception();
            CloseHandle(started.Thread);
            try
            {
                WaitForSingleObject(started.Process, Infinite);
                if (!GetExitCodeProcess(started.Process, out var code))
                    throw new Win32Exception();
                return (int)code;
            }
            finally
            {
                CloseHandle(started.Process);
            }
        }
        finally
        {
            CloseDesktop(desktop);
        }
    }

    // Refuses to go on anywhere but on a desktop Run made, so what must stay off the screen can't be drawn on it by a
    // direct call.
    public static void RequireCurrent()
    {
        var name = new StringBuilder(256);
        if (!GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), ObjectName, name, name.Capacity * 2, out _))
            throw new Win32Exception();
        if (!name.ToString().StartsWith(Prefix, StringComparison.Ordinal))
            throw new InvalidOperationException($"This runs on the desktop '{name}', which a person can see and use. Start it with --capture.");
    }

    private const string Prefix = "PrivateType.LayoutProbe.";
    private const int ObjectName = 2;
    private const uint GenericAll = 0x10000000;
    private const uint Infinite = 0xFFFFFFFF;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public short ShowWindow, Reserved2;
        public nint Reserved2Bytes, StandardInput, StandardOutput, StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Started
    {
        public nint Process, Thread;
        public int ProcessId, ThreadId;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateDesktop(string name, string? device, nint deviceMode, int flags, uint access, nint security);

    [DllImport("user32.dll")]
    private static extern bool CloseDesktop(nint desktop);

    [DllImport("user32.dll")]
    private static extern nint GetThreadDesktop(uint thread);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetUserObjectInformation(nint handle, int index, StringBuilder information, int bytes, out int needed);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(string program, StringBuilder commandLine, nint processSecurity, nint threadSecurity,
        bool inheritHandles, uint flags, nint environment, string? directory, ref StartupInfo startup, out Started started);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(nint handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(nint process, out uint code);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);
}
