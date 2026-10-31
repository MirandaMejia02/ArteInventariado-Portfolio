namespace ArteInventariado.Infrastructure.Installation;

/// <summary>
/// Preserves the original setup exception while exposing the precise stage
/// and persistent diagnostic path to the console/UAC caller.
/// </summary>
public sealed class MachineWideSetupFailureException : InvalidOperationException
{
    public MachineWideSetupFailureException(
        string stage,
        string diagnosticPath,
        Exception innerException)
        : base(
            $"La instalación falló en la etapa '{stage}'. " +
            $"Detalle: {innerException.Message} Diagnóstico: {diagnosticPath}",
            innerException)
    {
        Stage = stage;
        DiagnosticPath = diagnosticPath;
    }

    public string Stage { get; }

    public string DiagnosticPath { get; }
}
