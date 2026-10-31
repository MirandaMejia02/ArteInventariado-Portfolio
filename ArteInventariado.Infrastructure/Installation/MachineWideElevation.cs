using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace ArteInventariado.Infrastructure.Installation;

public enum MachineWideElevationOutcome
{
    AlreadyElevated = 1,
    RelaunchedElevated = 2,
    Cancelled = 3
}

public sealed record MachineWideElevationResult(
    MachineWideElevationOutcome Outcome,
    Process? Process = null);

/// <summary>
/// UAC is used only for explicit machine-wide operations.  Normal application
/// launches remain non-elevated; the machine-wide updater calls this helper
/// immediately before it needs to touch protected installation state.
/// </summary>
public static class MachineWideElevation
{
    public static bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            if (!principal.IsInRole(WindowsBuiltInRole.Administrator) || identity.AccessToken.IsInvalid)
            {
                return false;
            }

            // Membership in Administrators is not enough with UAC: an
            // administrator can still be running on a filtered, non-elevated
            // token.  Query TokenElevation so protected writes only proceed
            // from the consented updater process.
            return TryGetTokenElevation(out var elevated) && elevated;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or Win32Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads the current process token without conflating membership in the
    /// Administrators group with an elevated token.  Diagnostics use this to
    /// distinguish a filtered token from a failed UAC launch.
    /// </summary>
    public static bool TryGetTokenElevation(out bool elevated)
    {
        elevated = false;
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (identity.AccessToken.IsInvalid)
            {
                return false;
            }

            if (!GetTokenInformation(
                    identity.AccessToken,
                    TokenInformationClass.TokenElevation,
                    out var elevation,
                    Marshal.SizeOf<TokenElevation>(),
                    out _))
            {
                return false;
            }

            elevated = elevation.TokenIsElevated != 0;
            return true;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or Win32Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns whether the current token is suitable for a protected
    /// machine-wide operation.  The scheduled backup task runs as
    /// NT AUTHORITY\SYSTEM, whose token is privileged but is not required to
    /// be a member of the localized Administrators group.
    /// </summary>
    public static bool IsAdministratorOrSystem()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (identity.AccessToken.IsInvalid)
            {
                return false;
            }

            if (identity.User?.IsWellKnown(WellKnownSidType.LocalSystemSid) == true)
            {
                return true;
            }

            return IsAdministrator();
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or Win32Exception)
        {
            return false;
        }
    }

    public static MachineWideElevationResult EnsureElevated(
        string executablePath,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);
        EnsureNoShellExecuteEnvironment(environment);

        if (IsAdministrator())
        {
            return new MachineWideElevationResult(MachineWideElevationOutcome.AlreadyElevated);
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("La instalacion machine-wide requiere Windows.");
        }

        var executable = Path.GetFullPath(executablePath);
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException("No existe el ejecutable del instalador machine-wide.", executable);
        }

        var startInfo = CreateElevatedStartInfo(executable, arguments, environment);
        return MapProcessStart(() => Process.Start(startInfo));
    }

    /// <summary>
    /// Maps the ShellExecute result without swallowing non-UAC failures.  The
    /// small seam is also used by deterministic tests; production callers
    /// pass <see cref="Process.Start(ProcessStartInfo)"/> above.
    /// </summary>
    internal static MachineWideElevationResult MapProcessStart(Func<Process?> start)
    {
        ArgumentNullException.ThrowIfNull(start);
        try
        {
            var process = start()
                ?? throw new InvalidOperationException("Windows no pudo iniciar el instalador elevado.");
            return new MachineWideElevationResult(MachineWideElevationOutcome.RelaunchedElevated, process);
        }
        catch (Win32Exception ex) when (IsUacCancellation(ex))
        {
            // ERROR_CANCELLED: the user declined the UAC prompt.  No
            // installation method is called before this helper returns.
            return new MachineWideElevationResult(MachineWideElevationOutcome.Cancelled);
        }
    }

    /// <summary>
    /// ERROR_CANCELLED is the only ShellExecute/runas failure interpreted as
    /// an explicit UAC cancellation.  Other Win32 errors must surface with
    /// their original code and stage rather than being mislabeled as a user
    /// choice.
    /// </summary>
    internal static bool IsUacCancellation(Win32Exception exception) =>
        exception.NativeErrorCode == 1223;

    /// <summary>
    /// Builds the only ShellExecute/runas boundary used by machine-wide
    /// operations.  ArgumentList is intentionally used instead of a manually
    /// concatenated command line so paths cannot inject switches or commands.
    /// Windows does not allow environment overrides for this boundary, so a
    /// non-empty environment is rejected before Process.Start is attempted.
    /// </summary>
    public static ProcessStartInfo CreateElevatedStartInfo(
        string executablePath,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);
        EnsureNoShellExecuteEnvironment(environment);

        var executable = Path.GetFullPath(executablePath);
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = true,
            Verb = "runas"
        };
        foreach (var argument in arguments)
        {
            if (argument is null)
            {
                throw new ArgumentException("La lista de argumentos no puede contener valores nulos.", nameof(arguments));
            }

            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static void EnsureNoShellExecuteEnvironment(
        IReadOnlyDictionary<string, string>? environment)
    {
        if (environment is { Count: > 0 })
        {
            throw new InvalidOperationException(
                "El límite UAC runas no admite variables de entorno. " +
                "Transfiera únicamente estado no sensible mediante argumentos validados.");
        }
    }

    private enum TokenInformationClass
    {
        TokenElevation = 20
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenElevation
    {
        public int TokenIsElevated;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        SafeAccessTokenHandle tokenHandle,
        TokenInformationClass tokenInformationClass,
        out TokenElevation tokenInformation,
        int tokenInformationLength,
        out int returnLength);
}
