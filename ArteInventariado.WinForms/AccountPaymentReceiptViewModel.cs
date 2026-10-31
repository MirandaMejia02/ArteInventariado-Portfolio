using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public sealed record AccountPaymentReceiptViewModel(
    int AbonoCuentaPorCobrarId,
    string CodigoAbono,
    DateTime FechaAbono,
    string Cliente,
    string CodigoCuenta,
    string NumeroVenta,
    string Usuario,
    MetodoPago MetodoPago,
    string? Referencia,
    long SaldoAnteriorCentavos,
    long MontoCentavos,
    long AplicadoInteresCentavos,
    long AplicadoCapitalCentavos,
    long SaldoNuevoCentavos,
    EstadoCuentaPorCobrar EstadoCuenta,
    DateTime FechaVencimiento,
    SituacionCrediticia SituacionCuenta,
    string? Observaciones)
{
    public static AccountPaymentReceiptViewModel FromDetail(AccountPaymentDetail detail)
    {
        var summary = detail.Summary;
        return new AccountPaymentReceiptViewModel(
            summary.AbonoCuentaPorCobrarId,
            summary.CodigoAbono,
            summary.FechaAbono,
            $"{summary.CodigoCliente} - {summary.Cliente}",
            summary.CodigoCuenta,
            summary.NumeroVenta,
            summary.Usuario,
            summary.MetodoPago,
            summary.Referencia,
            summary.SaldoAnteriorCentavos,
            summary.MontoCentavos,
            summary.AplicadoInteresCentavos,
            summary.AplicadoCapitalCentavos,
            summary.SaldoNuevoCentavos,
            detail.EstadoCuenta,
            detail.FechaVencimiento,
            detail.SituacionCuenta,
            summary.Observaciones);
    }

    public string SuggestedPdfFileName =>
        $"Abono_{SanitizeFileName(CodigoAbono)}_{SaleReceiptViewModel.ToLocal(FechaAbono):yyyyMMdd_HHmm}.pdf";

    public IReadOnlyList<string> BuildReceiptLines()
    {
        var dueLocal = SaleReceiptViewModel.ToLocal(FechaVencimiento).Date;
        var lateDays = Math.Max(0, (DateTime.Today - dueLocal).Days);
        var lines = new List<string>
        {
            UITheme.BusinessName,
            "Comprobante interno de abono",
            string.Empty,
            $"Codigo del abono: {CodigoAbono}",
            $"Fecha y hora: {SaleReceiptViewModel.ToLocal(FechaAbono):yyyy-MM-dd HH:mm}",
            $"Cliente: {Cliente}",
            $"Cuenta por cobrar: {CodigoCuenta}",
            $"Venta relacionada: {NumeroVenta}",
            $"Usuario: {Usuario}",
            string.Empty,
            $"Metodo: {MetodoPago}",
            $"Referencia: {Referencia ?? "-"}",
            $"Observaciones: {Observaciones ?? "-"}",
            string.Empty,
            $"Saldo anterior: {SaleReceiptViewModel.FormatDollars(SaldoAnteriorCentavos)}",
            $"Monto abonado: {SaleReceiptViewModel.FormatDollars(MontoCentavos)}",
            $"Aplicado a interes: {SaleReceiptViewModel.FormatDollars(AplicadoInteresCentavos)}",
            $"Aplicado a capital: {SaleReceiptViewModel.FormatDollars(AplicadoCapitalCentavos)}",
            $"Saldo nuevo: {SaleReceiptViewModel.FormatDollars(SaldoNuevoCentavos)}",
            string.Empty,
            $"Estado de la cuenta: {EstadoCuenta}",
            $"Situacion: {SituacionCuenta}",
            $"Fecha de vencimiento: {dueLocal:yyyy-MM-dd}",
            $"Dias de atraso: {lateDays}",
            string.Empty,
            "Este documento es un comprobante interno de abono.",
            "No es factura fiscal."
        };

        return lines;
    }

    public string BuildReceiptText()
    {
        return string.Join(Environment.NewLine, BuildReceiptLines());
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
