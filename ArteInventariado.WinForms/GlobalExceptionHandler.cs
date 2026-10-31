using System.Threading;

namespace ArteInventariado.WinForms;

internal static class GlobalExceptionHandler
{
    public static void Register()
    {
        System.Windows.Forms.Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        System.Windows.Forms.Application.ThreadException += OnThreadException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    private static void OnThreadException(object sender, ThreadExceptionEventArgs args)
    {
        TechnicalErrorLogger.Log("ui-thread-unhandled", args.Exception);
        ShowFriendlyMessage(args.Exception);
    }

    private static void OnUnhandledException(object? sender, UnhandledExceptionEventArgs args)
    {
        var exception = args.ExceptionObject as Exception
            ?? new InvalidOperationException("Se produjo un error no administrado sin detalles.");
        TechnicalErrorLogger.Log("appdomain-unhandled", exception);

        if (!Environment.UserInteractive)
        {
            return;
        }

        try
        {
            ShowFriendlyMessage(exception);
        }
        catch
        {
            // El proceso puede estar terminando; el detalle ya se intento guardar en el log.
        }
    }

    private static void ShowFriendlyMessage(Exception exception)
    {
        var persistenceFailure = ContainsPersistenceFailure(exception);
        var message = persistenceFailure
            ? "No fue posible guardar la operacion. Cierre y vuelva a abrir el modulo. " +
              "Si el problema persiste, reinicie la aplicacion. El detalle tecnico fue guardado en el registro de errores."
            : "Se produjo un error inesperado. El detalle tecnico fue guardado en el registro de errores.";

        MessageBox.Show(
            message,
            "ArteInventariado",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private static bool ContainsPersistenceFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.GetType().Name is "DbUpdateException" or "DbUpdateConcurrencyException")
            {
                return true;
            }
        }

        return false;
    }
}
