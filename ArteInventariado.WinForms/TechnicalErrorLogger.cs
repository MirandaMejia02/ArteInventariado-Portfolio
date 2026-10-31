namespace ArteInventariado.WinForms;

internal static class TechnicalErrorLogger
{
    public static void Log(string area, Exception exception)
    {
        try
        {
            var directory = ArteInventariado.Infrastructure.ArteInventariadoPathProvider
                .CreateDefault().LogsRoot;
            Directory.CreateDirectory(directory);
            var activeForm = Form.ActiveForm;
            var context = $"Version={System.Windows.Forms.Application.ProductVersion}; " +
                $"Runtime={Environment.Version}; " +
                $"Form={activeForm?.Name ?? "<none>"}; " +
                $"Title={activeForm?.Text ?? "<none>"}";
            var diagnosticData = FormatDiagnosticData(exception);
            File.AppendAllText(
                Path.Combine(directory, "application-errors.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {area}{Environment.NewLine}" +
                $"{context}{Environment.NewLine}{exception}{diagnosticData}" +
                $"{Environment.NewLine}{new string('-', 80)}{Environment.NewLine}");
        }
        catch (Exception loggingException)
        {
            System.Diagnostics.Debug.WriteLine($"No se pudo escribir el log tecnico: {loggingException}");
        }
    }

    private static string FormatDiagnosticData(Exception exception)
    {
        var lines = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            foreach (System.Collections.DictionaryEntry item in current.Data)
            {
                lines.Add($"{item.Key}: {item.Value}");
            }
        }

        return lines.Count == 0
            ? string.Empty
            : $"{Environment.NewLine}Diagnostic data:{Environment.NewLine}{string.Join(Environment.NewLine, lines)}";
    }
}
