using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.Backup;
using ArteInventariado.Application.Models;

if (!OperatingSystem.IsWindows())
{
    MachineWideBackupDiagnostics.RecordEarlyFailure(
        null,
        "plataforma",
        new PlatformNotSupportedException("El BackupRunner protegido solo puede ejecutarse en Windows."));
    Console.Error.WriteLine("El BackupRunner protegido solo puede ejecutarse en Windows.");
    return 10;
}

if (args.Length == 1 && string.Equals(args[0], "--smoke", StringComparison.OrdinalIgnoreCase))
{
    return MachineWideBackupSmokeHarness.Run();
}

var backupKind = BackupKind.Automatic;
Guid? operationId = null;
var scheduledRun = args.Length == 1 &&
    string.Equals(args[0], "--run", StringComparison.OrdinalIgnoreCase);
var manualRun = false;
if (args.Length == 3 &&
    string.Equals(args[0], "--manual", StringComparison.OrdinalIgnoreCase) &&
    string.Equals(args[1], "--request-id", StringComparison.OrdinalIgnoreCase) &&
    Guid.TryParseExact(args[2], "D", out var parsedOperationId) &&
    parsedOperationId != Guid.Empty)
{
    manualRun = true;
    backupKind = BackupKind.Manual;
    operationId = parsedOperationId;
}

if (!scheduledRun && !manualRun)
{
    MachineWideBackupDiagnostics.RecordEarlyFailure(
        null,
        "argumentos",
        new ArgumentException("El BackupRunner requiere --run o una solicitud manual con identificador válido."));
    Console.Error.WriteLine("Uso interno: ArteInventariado.BackupRunner.exe --run | --manual --request-id <guid>");
    return 2;
}

ArteInventariadoPathProvider? bootstrapPaths = null;
var bootstrapStage = "entrada";
return await MachineWideBackupProcess.RunAsync(
    () =>
    {
        // The production task supplies no roots or destinations. They are fixed
        // by the machine-wide provider and validated again by the runner.
        bootstrapStage = "resolucion-rutas";
        bootstrapPaths = ArteInventariadoPathProvider.CreateMachineWideDefault();
        bootstrapStage = "configuracion-temp-sqlite";
        MachineWideSqliteRuntime.Configure(bootstrapPaths);
        bootstrapStage = "construccion-runner";
        var runner = new MachineWideBackupRunner(bootstrapPaths);
        bootstrapStage = "entrada-runner";
        return runner.RunAsync(backupKind, operationId);
    },
    Console.Out,
    Console.Error,
    exception =>
    {
        // RunAsync owns failures after the runner has been entered.  The
        // process boundary still needs to make provider/constructor failures
        // observable and mark the status as Failure when its roots are known.
        if (!string.Equals(bootstrapStage, "entrada-runner", StringComparison.Ordinal))
        {
            MachineWideBackupDiagnostics.RecordEarlyFailure(
                bootstrapPaths,
                bootstrapStage,
                exception);
        }
    });
