using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace PrivateType.App;

internal enum StartupOwnershipDecision
{
    LeaveDisabled,
    KeepRegistered,
    ClaimCurrent,
    ConfirmCurrent
}

internal enum StartupPreferenceUpdate
{
    NoChange,
    ClaimCurrent,
    Disable
}

internal static class StartupOwnershipPolicy
{
    internal static StartupOwnershipDecision Decide(
        bool hasPrivateTypeRegistration,
        bool hasLegacyRegistration,
        string? registeredExecutablePath,
        Version? registeredVersion,
        string currentExecutablePath,
        Version? currentVersion)
    {
        if (!hasPrivateTypeRegistration && !hasLegacyRegistration)
            return StartupOwnershipDecision.LeaveDisabled;

        if (hasPrivateTypeRegistration
            && string.Equals(registeredExecutablePath, currentExecutablePath, StringComparison.OrdinalIgnoreCase))
        {
            return hasLegacyRegistration
                ? StartupOwnershipDecision.ClaimCurrent
                : StartupOwnershipDecision.KeepRegistered;
        }

        if (!hasPrivateTypeRegistration && hasLegacyRegistration)
            return StartupOwnershipDecision.ClaimCurrent;

        if (currentVersion is null || registeredVersion is null)
            return StartupOwnershipDecision.ConfirmCurrent;

        return currentVersion.CompareTo(registeredVersion) >= 0
            ? StartupOwnershipDecision.ClaimCurrent
            : StartupOwnershipDecision.ConfirmCurrent;
    }
}

internal static class StartupPreferencePolicy
{
    internal static StartupPreferenceUpdate DecideUpdate(bool wasEnabled, bool requestedEnabled)
    {
        if (wasEnabled == requestedEnabled)
            return StartupPreferenceUpdate.NoChange;

        return requestedEnabled
            ? StartupPreferenceUpdate.ClaimCurrent
            : StartupPreferenceUpdate.Disable;
    }
}

internal interface IStartupRegistrationWriter
{
    StartupRegistrationSnapshot Capture();
    void Claim(string executablePath);
    void Disable();
    void Restore(StartupRegistrationSnapshot snapshot);
}

internal sealed class StartupRegistrationRestoreException(Exception saveFailure, Exception restoreFailure)
    : Exception(
        "Settings were not saved, and the previous Windows startup version could not be restored.",
        new AggregateException(saveFailure, restoreFailure));

internal static class StartupPreferenceTransaction
{
    internal static void Apply(
        StartupPreferenceUpdate update,
        IStartupRegistrationWriter registration,
        string currentExecutablePath,
        Action saveSettings)
    {
        StartupRegistrationSnapshot? snapshot = null;
        try
        {
            if (update != StartupPreferenceUpdate.NoChange)
            {
                snapshot = registration.Capture();
                if (update == StartupPreferenceUpdate.ClaimCurrent)
                    registration.Claim(currentExecutablePath);
                else
                    registration.Disable();
            }

            saveSettings();
        }
        catch (Exception saveFailure)
        {
            if (snapshot is null)
                throw;

            try
            {
                registration.Restore(snapshot);
            }
            catch (Exception restoreFailure)
            {
                throw new StartupRegistrationRestoreException(saveFailure, restoreFailure);
            }

            throw;
        }
    }
}

internal sealed record StartupRegistrationSnapshot(string? PrivateTypeCommand, string? LegacyCommand)
{
    internal bool HasPrivateTypeRegistration => PrivateTypeCommand is not null;
    internal bool HasLegacyRegistration => LegacyCommand is not null;
}

internal sealed record StartupRegistrationTarget(string? ExecutablePath, Version? Version);

internal sealed class WindowsStartupRegistration : IStartupRegistrationWriter
{
    private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    private const string ValueName = "PrivateType";
    private const string LegacyValueName = "LiveDictation";

    public StartupRegistrationSnapshot Capture()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return new StartupRegistrationSnapshot(
            key?.GetValue(ValueName) as string,
            key?.GetValue(LegacyValueName) as string);
    }

    public void RemoveLegacy()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Windows Startup settings are unavailable for this user.");

        key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
    }

    public StartupRegistrationTarget ReadTarget(StartupRegistrationSnapshot snapshot)
    {
        var command = snapshot.PrivateTypeCommand ?? snapshot.LegacyCommand;
        var executablePath = ExecutablePathFrom(command);
        return new StartupRegistrationTarget(executablePath, VersionOf(executablePath));
    }

    public void Claim(string executablePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Windows Startup settings are unavailable for this user.");

        key.SetValue(ValueName, Quote(executablePath), RegistryValueKind.String);
        key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Windows Startup settings are unavailable for this user.");

        key.DeleteValue(ValueName, throwOnMissingValue: false);
        key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
    }

    public void Restore(StartupRegistrationSnapshot snapshot)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Windows Startup settings are unavailable for this user.");

        RestoreValue(key, ValueName, snapshot.PrivateTypeCommand);
        RestoreValue(key, LegacyValueName, snapshot.LegacyCommand);
    }

    internal static string Quote(string executablePath) => $"\"{executablePath}\"";

    internal static string? ExecutablePathFrom(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        var trimmed = command.Trim();
        if (!trimmed.StartsWith('"'))
            return File.Exists(trimmed) ? trimmed : null;

        var closingQuote = trimmed.IndexOf('"', 1);
        return closingQuote > 1 ? trimmed[1..closingQuote] : null;
    }

    internal static Version? VersionOf(string? executablePath)
    {
        if (executablePath is null || !File.Exists(executablePath))
            return null;

        return Version.TryParse(FileVersionInfo.GetVersionInfo(executablePath).FileVersion, out var version)
            ? version
            : null;
    }

    private static void RestoreValue(RegistryKey key, string name, string? command)
    {
        if (command is null)
            key.DeleteValue(name, throwOnMissingValue: false);
        else
            key.SetValue(name, command, RegistryValueKind.String);
    }
}
