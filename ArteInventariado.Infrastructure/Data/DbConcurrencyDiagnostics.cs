using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace ArteInventariado.Infrastructure.Data;

internal static class DbConcurrencyDiagnostics
{
    public static async Task<string> BuildAsync(
        DbUpdateConcurrencyException exception,
        string operation,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        output.AppendLine($"Operation={operation}");
        output.AppendLine($"ConflictingEntries={exception.Entries.Count}");

        foreach (var entry in exception.Entries)
        {
            output.AppendLine($"Entity={entry.Metadata.ClrType.Name}; State={entry.State}");
            output.AppendLine($"Key={FormatKey(entry)}");
            output.AppendLine($"Original={FormatValues(entry.OriginalValues)}");
            output.AppendLine($"Current={FormatValues(entry.CurrentValues)}");

            try
            {
                var databaseValues = await entry.GetDatabaseValuesAsync(cancellationToken);
                output.AppendLine(databaseValues is null
                    ? "Database=<row-not-found>"
                    : $"Database={FormatValues(databaseValues)}");
            }
            catch (Exception readException)
            {
                output.AppendLine($"Database=<read-failed:{readException.GetType().Name}>");
            }
        }

        return output.ToString().TrimEnd();
    }

    public static async Task<InvalidOperationException> WrapAsync(
        DbUpdateConcurrencyException exception,
        string operation,
        string userMessage,
        CancellationToken cancellationToken)
    {
        var wrapped = new InvalidOperationException(userMessage, exception);
        wrapped.Data["EFConcurrencyDiagnostics"] = await BuildAsync(
            exception,
            operation,
            cancellationToken);
        return wrapped;
    }

    private static string FormatKey(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        return key is null
            ? "<no-primary-key>"
            : string.Join(", ", key.Properties.Select(property =>
                $"{property.Name}={FormatValue(entry.Property(property.Name).CurrentValue)}"));
    }

    private static string FormatValues(PropertyValues values) => string.Join(", ",
        values.Properties.Select(property =>
            $"{property.Name}={FormatProperty(property.Name, values[property])}"));

    private static string FormatProperty(string propertyName, object? value)
    {
        var sensitiveNames = new[]
        {
            "Clave", "Password", "Correo", "Telefono", "Direccion", "Referencia",
            "Motivo", "Descripcion", "Observacion", "Nombre"
        };

        return sensitiveNames.Any(name => propertyName.Contains(name, StringComparison.OrdinalIgnoreCase))
            ? "<redacted>"
            : FormatValue(value);
    }

    private static string FormatValue(object? value) => value switch
    {
        null => "<null>",
        DateTime date => date.ToString("O"),
        byte[] => "<binary>",
        _ => value.ToString() ?? "<null>"
    };
}
