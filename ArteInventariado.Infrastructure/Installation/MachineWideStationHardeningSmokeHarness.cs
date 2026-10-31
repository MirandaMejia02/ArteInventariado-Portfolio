using System.Text.Json;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.Station;

namespace ArteInventariado.Infrastructure.Installation;

/// <summary>
/// Synthetic smoke for the station identity boundary. It uses temporary
/// roots only, never invokes UAC, and leaves no installation or business data
/// behind.
/// </summary>
public static class MachineWideStationHardeningSmokeHarness
{
    public static int Run()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"ArteInventariado-station-hardening-smoke-{Guid.NewGuid():N}");
        try
        {
            var paths = ArteInventariadoPathProvider.CreateMachineWide(
                Path.Combine(root, "install"),
                Path.Combine(root, "data"),
                Path.Combine(root, "user"));
            var store = new LocalStationConfigurationStore(paths);
            var pending = store.EnsureInitialized();
            var installationId = pending.Status.InstallationId
                ?? throw new InvalidDataException("El smoke no obtuvo installationId.");
            store.SaveConfigured(InstallationMode.Principal);
            store.SavePosSettings(new PosStationSettingsDocument(true));

            MachineWideStationConfigurationHelper.ValidateManagedRoots(paths, allowSyntheticRoots: true);
            using var core = JsonDocument.Parse(File.ReadAllText(paths.StationSettingsPath));
            Require(core.RootElement.TryGetProperty("installationId", out var id) &&
                    id.GetGuid() == installationId,
                "el core no conserva installationId");
            Require(!core.RootElement.TryGetProperty("pos", out _),
                "el core contiene preferencias operativas");

            var operational = JsonDocument.Parse(File.ReadAllText(paths.StationOperationalSettingsPath));
            using (operational)
            {
                Require(operational.RootElement.GetProperty("pos").GetProperty("quickPosEnabled").GetBoolean(),
                    "las preferencias operativas no se guardaron");
            }

            var coreBefore = File.ReadAllText(paths.StationSettingsPath);
            var helper = Path.Combine(paths.MachineInstallRoot, ArteInventariadoPathProvider.StationHelperFileName);
            Directory.CreateDirectory(paths.MachineInstallRoot);
            File.WriteAllText(helper, "synthetic helper");
            var cancelled = new MachineWideStationModeService(
                paths,
                (_, _) => new MachineWideElevationResult(MachineWideElevationOutcome.Cancelled),
                allowSyntheticRoots: true);
            try
            {
                cancelled.ChangeModeAsync(
                    InstallationMode.Consulta,
                    installationId,
                    InstallationMode.Principal).GetAwaiter().GetResult();
                return 1;
            }
            catch (OperationCanceledException)
            {
                Require(coreBefore == File.ReadAllText(paths.StationSettingsPath),
                    "UAC cancelado modificó el core");
            }

            return 0;
        }
        catch
        {
            return 1;
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
