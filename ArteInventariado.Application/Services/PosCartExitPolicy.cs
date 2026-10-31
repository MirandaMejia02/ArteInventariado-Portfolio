namespace ArteInventariado.Application.Services;

public enum PosExitReason
{
    CloseWindow,
    ReturnToDashboard,
    Logout,
    ApplicationExit
}

public sealed record PosCartExitSummary(int DistinctProducts, int TotalUnits)
{
    public bool IsEmpty => DistinctProducts == 0 || TotalUnits == 0;
}

public static class PosCartExitPolicy
{
    public static string BuildMessage(PosCartExitSummary summary, PosExitReason reason)
    {
        var destination = DescribeDestination(reason);
        var productWord = DescribeProductWord(summary.DistinctProducts);
        var unitWord = DescribeUnitWord(summary.TotalUnits);
        return $"Hay {summary.DistinctProducts} {productWord} ({summary.TotalUnits} {unitWord}) en la venta actual.\r\n" +
               $"Si decide {destination}, el carrito será descartado.";
    }

    public static string BuildMultiDraftMessage(
        int draftCount,
        PosCartExitSummary summary,
        PosExitReason reason)
    {
        if (draftCount <= 1)
        {
            return BuildMessage(summary, reason);
        }

        var destination = DescribeDestination(reason);
        var productWord = DescribeProductWord(summary.DistinctProducts);
        var unitWord = DescribeUnitWord(summary.TotalUnits);
        return $"Hay {draftCount} ventas pendientes con {summary.DistinctProducts} {productWord} " +
               $"({summary.TotalUnits} {unitWord}) en preparación.\r\n" +
               $"Si decide {destination}, las ventas serán descartadas.";
    }

    public static bool RequiresConfirmation(
        PosCartExitSummary summary,
        bool controlledShutdown,
        bool alreadyConfirmed) =>
        !summary.IsEmpty && !controlledShutdown && !alreadyConfirmed;

    public static bool TryExit(
        PosCartExitSummary summary,
        bool controlledShutdown,
        bool alreadyConfirmed,
        Func<bool> confirmDiscard,
        Action discard)
    {
        if (!RequiresConfirmation(summary, controlledShutdown, alreadyConfirmed))
        {
            return true;
        }

        if (!confirmDiscard())
        {
            return false;
        }

        discard();
        return true;
    }

    private static string DescribeDestination(PosExitReason reason) => reason switch
    {
        PosExitReason.Logout => "cerrar la sesión",
        PosExitReason.ApplicationExit => "cerrar ArteInventariado",
        _ => "salir del punto de venta"
    };

    private static string DescribeProductWord(int count) => count == 1 ? "producto" : "productos";

    private static string DescribeUnitWord(int count) => count == 1 ? "unidad" : "unidades";
}
