namespace ArteInventariado.Infrastructure.Installation;

/// <summary>
/// Reads path values from the bootstrap command line. Windows normally
/// preserves a quoted path as one argument; joining consecutive non-switch
/// tokens also keeps the explicit bootstrap usable when a legacy shell caller
/// omitted quotes around a path containing spaces.
/// </summary>
public static class MachineWideBootstrapArgumentReader
{
    public static string? GetPathValue(
        IReadOnlyList<string> arguments,
        string name)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        for (var index = 0; index < arguments.Count; index++)
        {
            if (!string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var values = new List<string>();
            for (var valueIndex = index + 1;
                 valueIndex < arguments.Count &&
                 !string.IsNullOrWhiteSpace(arguments[valueIndex]) &&
                 !arguments[valueIndex].StartsWith("--", StringComparison.Ordinal);
                 valueIndex++)
            {
                values.Add(arguments[valueIndex]);
            }

            if (values.Count == 0)
            {
                return null;
            }

            var value = string.Join(" ", values).Trim();
            return value.Length >= 2 && value[0] == '"' && value[^1] == '"'
                ? value[1..^1]
                : value;
        }

        return null;
    }
}
