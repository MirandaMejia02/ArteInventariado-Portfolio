using ArteInventariado.Infrastructure.Updater;

namespace ArteInventariado.Infrastructure.Installation;

/// <summary>
/// Cross-process exclusion for operations that can replace the business DB
/// or mutate the machine-wide installation. The file remains in the
/// protected recovery directory; exclusivity is provided by its open handle.
/// </summary>
public static class MachineWideMaintenanceLease
{
    private const string LeaseFileName = "machine-wide-maintenance.lock";

    public static IDisposable Acquire(ArteInventariadoPathProvider paths) =>
        Acquire(paths, allowSyntheticRoots: false);

    internal static IDisposable Acquire(
        ArteInventariadoPathProvider paths,
        bool allowSyntheticRoots)
    {
        ArgumentNullException.ThrowIfNull(paths);
        MachineWideStationConfigurationHelper.ValidateManagedRoots(paths, allowSyntheticRoots);
        MachineWideUpdateGuard.ValidateManagedRoots(UpdateRuntimePaths.Create(paths));

        Directory.CreateDirectory(paths.RecoveryRoot);
        var leasePath = Path.Combine(paths.RecoveryRoot, LeaseFileName);
        if (File.Exists(leasePath) &&
            (File.GetAttributes(leasePath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("El bloqueo de mantenimiento protegido no puede ser un reparse point.");
        }

        try
        {
            return new FileStream(
                leasePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.WriteThrough);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException(
                "Ya hay una actualización o un reinicio de base de datos en curso en esta instalación.",
                exception);
        }
    }
}
