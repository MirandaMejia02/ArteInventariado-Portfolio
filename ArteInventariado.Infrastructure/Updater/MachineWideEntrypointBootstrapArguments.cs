namespace ArteInventariado.Infrastructure.Updater;

/// <summary>
/// Strict command-line contract for the one-time entrypoint bootstrap used
/// when an older installed Launcher/Updater cannot complete the normal
/// machine-wide handoff.  This is intentionally separate from the normal
/// updater arguments and never accepts an update-state or PRE_UPDATE path.
/// </summary>
public sealed record MachineWideEntrypointBootstrapArguments(
    string MachineInstallRoot,
    string MachineDataRoot,
    string UserDataRoot,
    string ManifestPath,
    string PackagePath)
{
    public static MachineWideEntrypointBootstrapArguments Parse(
        IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var commandSeen = false;
        var knownValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "--machine-install-root",
            "--machine-data-root",
            "--user-data-root",
            "--manifest",
            "--package"
        };

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (string.Equals(argument, "--machine-entrypoint-bootstrap", StringComparison.OrdinalIgnoreCase))
            {
                if (commandSeen)
                {
                    throw new ArgumentException("El comando de bootstrap de entrypoints está duplicado.");
                }

                commandSeen = true;
                continue;
            }

            if (!knownValues.Contains(argument))
            {
                throw new ArgumentException(
                    $"El bootstrap de entrypoints recibió un argumento no reconocido: {argument}.");
            }

            if (!values.TryAdd(argument, ReadValue(arguments, ref index, argument)))
            {
                throw new ArgumentException($"El argumento {argument} está duplicado.");
            }
        }

        if (!commandSeen)
        {
            throw new ArgumentException("Falta --machine-entrypoint-bootstrap.");
        }

        return new MachineWideEntrypointBootstrapArguments(
            Required(values, "--machine-install-root"),
            Required(values, "--machine-data-root"),
            Required(values, "--user-data-root"),
            Required(values, "--manifest"),
            Required(values, "--package"));
    }

    private static string ReadValue(
        IReadOnlyList<string> arguments,
        ref int index,
        string name)
    {
        if (index + 1 >= arguments.Count ||
            string.IsNullOrWhiteSpace(arguments[index + 1]) ||
            arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Falta el valor de {name}.");
        }

        index++;
        return arguments[index];
    }

    private static string Required(
        IReadOnlyDictionary<string, string> values,
        string name) =>
        values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Falta el argumento requerido {name}.");
}
