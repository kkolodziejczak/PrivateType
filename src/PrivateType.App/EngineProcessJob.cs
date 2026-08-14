using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PrivateType.App;

internal sealed class EngineProcessJob : IDisposable
{
    internal const uint KillOnJobClose = 0x00002000;
    internal const uint AppOwnedEngineLimitFlags = KillOnJobClose;
    private nint handle;

    public EngineProcessJob()
    {
        handle = CreateJobObject(nint.Zero, null);
        if (handle == nint.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the app-owned engine job.");

        var limits = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation { LimitFlags = AppOwnedEngineLimitFlags }
        };
        if (!SetInformationJobObject(handle, JobObjectInfoClass.ExtendedLimitInformation, ref limits, Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
            throw CreateException("Could not configure the app-owned engine job.");
    }

    public void Assign(Process engineProcess)
    {
        ObjectDisposedException.ThrowIf(handle == nint.Zero, this);
        if (!AssignProcessToJobObject(handle, engineProcess.Handle))
            throw CreateException("Could not attach the local engine to the app-owned job.");
    }

    public void Dispose()
    {
        if (handle == nint.Zero)
            return;

        CloseHandle(handle);
        handle = nint.Zero;
    }

    private static Win32Exception CreateException(string message) => new(Marshal.GetLastWin32Error(), message);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateJobObject(nint jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(nint job, JobObjectInfoClass infoClass, ref JobObjectExtendedLimitInformation info, int infoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(nint job, nint process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    private enum JobObjectInfoClass
    {
        ExtendedLimitInformation = 9
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public nint ProcessMemoryLimit;
        public nint JobMemoryLimit;
        public nint PeakProcessMemoryUsed;
        public nint PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nint MinimumWorkingSetSize;
        public nint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }
}
