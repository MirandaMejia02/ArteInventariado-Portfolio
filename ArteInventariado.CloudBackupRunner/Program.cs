using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.CloudBackup;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("El CloudBackupRunner protegido solo puede ejecutarse en Windows.");
    return 10;
}

if (args.Length != 1 || !string.Equals(args[0], "--run", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Uso interno: ArteInventariado.CloudBackupRunner.exe --run");
    return 2;
}

try
{
    // Production roots are fixed by the machine-wide provider. No arbitrary
    // bucket, root, destination, or credential is accepted through the CLI.
    var paths = ArteInventariadoPathProvider.CreateMachineWideDefault();
    var result = await new CloudBackupRunner(paths).RunAsync();
    Console.WriteLine($"{result.Message} Resultado={result.Outcome}.");
    return result.Outcome is CloudBackupRunOutcome.Succeeded
        or CloudBackupRunOutcome.SkippedDisabled
        or CloudBackupRunOutcome.SkippedConcurrent
        or CloudBackupRunOutcome.NoCandidates
        ? 0
        : 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"CloudBackupRunner fallido: {ex.Message}");
    return 1;
}
