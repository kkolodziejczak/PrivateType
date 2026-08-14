using System.Runtime.InteropServices;

namespace PrivateType.App;

internal static class WindowsProcessIntegrity
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;

    internal static bool CanReceiveInputFromCurrentProcess(uint targetProcessId)
    {
        return TryGetIntegrityLevel((uint)Environment.ProcessId, out var currentLevel)
            && TryGetIntegrityLevel(targetProcessId, out var targetLevel)
            && !IsHigher(targetLevel, currentLevel);
    }

    internal static bool IsHigher(uint targetLevel, uint currentLevel)
    {
        return targetLevel > currentLevel;
    }

    private static bool TryGetIntegrityLevel(uint processId, out uint integrityLevel)
    {
        integrityLevel = 0;
        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == nint.Zero)
            return false;

        try
        {
            if (!OpenProcessToken(process, TokenQuery, out var token))
                return false;

            try
            {
                GetTokenInformation(token, TokenIntegrityLevel, nint.Zero, 0, out var length);
                if (length == 0)
                    return false;

                var buffer = Marshal.AllocHGlobal((int)length);
                try
                {
                    if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, length, out _))
                        return false;

                    var sid = Marshal.ReadIntPtr(buffer);
                    var subAuthorityCount = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                    if (subAuthorityCount == 0)
                        return false;

                    integrityLevel = unchecked((uint)Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(subAuthorityCount - 1))));
                    return true;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(nint tokenHandle, int tokenInformationClass, nint tokenInformation, uint tokenInformationLength, out uint returnLength);

    [DllImport("advapi32.dll")]
    private static extern nint GetSidSubAuthorityCount(nint sid);

    [DllImport("advapi32.dll")]
    private static extern nint GetSidSubAuthority(nint sid, uint subAuthority);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
