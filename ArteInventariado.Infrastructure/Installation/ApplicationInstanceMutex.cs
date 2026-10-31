using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace ArteInventariado.Infrastructure.Installation;

/// <summary>
/// Coordinates the interactive WinForms process across Windows accounts.
/// A versioned name avoids opening the legacy mutex whose ACL may have been
/// created by another account or elevation context.
/// </summary>
public static class ApplicationInstanceMutex
{
    public const string InteractiveMutexName = "Global\\ArteInventariado.WinForms.Interactive.v2";
    public const string LegacyInteractiveMutexName = "Global\\ArteInventariado.WinForms.Interactive";

    [SupportedOSPlatform("windows")]
    private const MutexRights InteractiveAccessRights = MutexRights.Synchronize | MutexRights.Modify;

    [SupportedOSPlatform("windows")]
    public static Mutex CreateInteractive(bool initiallyOwned, out bool createdNew) =>
        CreateNamed(initiallyOwned, InteractiveMutexName, out createdNew);

    [SupportedOSPlatform("windows")]
    internal static Mutex CreateNamed(bool initiallyOwned, string name, out bool createdNew)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The interactive application mutex is Windows-only.");
        }

        try
        {
            var existing = MutexAcl.OpenExisting(name, InteractiveAccessRights);
            createdNew = false;
            return existing;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            try
            {
                return MutexAcl.Create(initiallyOwned, name, out createdNew, CreateInteractiveSecurity());
            }
            catch (UnauthorizedAccessException)
            {
                // Another process may have created the mutex between OpenExisting and Create.
                // Reopen it with only the rights required to wait and release it.
                var existing = MutexAcl.OpenExisting(name, InteractiveAccessRights);
                createdNew = false;
                return existing;
            }
        }
    }

    [SupportedOSPlatform("windows")]
    internal static MutexSecurity CreateInteractiveSecurity()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The interactive application mutex security is Windows-only.");
        }

        var security = new MutexSecurity();
        AddAccessRule(security, WellKnownSidType.BuiltinUsersSid);
        AddAccessRule(security, WellKnownSidType.BuiltinAdministratorsSid);
        AddAccessRule(security, WellKnownSidType.LocalSystemSid);
        return security;
    }

    [SupportedOSPlatform("windows")]
    private static void AddAccessRule(MutexSecurity security, WellKnownSidType sidType)
    {
        var identity = new SecurityIdentifier(sidType, null);
        security.AddAccessRule(new MutexAccessRule(
            identity,
            MutexRights.Synchronize | MutexRights.Modify,
            AccessControlType.Allow));
    }
}
