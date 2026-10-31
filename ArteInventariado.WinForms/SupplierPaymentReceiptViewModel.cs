using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public sealed record SupplierPaymentReceiptViewModel(
    string Codigo,
    string Tipo,
    DateTime Fecha,
    string Proveedor,
    string CodigoCuenta,
    string CodigoCompra,
    string Usuario,
    string Metodo,
    string Origen,
    string Responsable,
    string EfectoCaja,
    string? Referencia,
    long MontoCentavos,
    long SaldoAnteriorCentavos,
    long SaldoNuevoCentavos,
    string? Observaciones)
{
    public static SupplierPaymentReceiptViewModel FromSummary(SupplierPaymentSummary summary) => new(
        summary.CodigoAbono, "ABONO", summary.FechaAbono, $"{summary.CodigoProveedor} - {summary.Proveedor}",
        summary.CodigoCuenta, summary.CodigoCompra, summary.Usuario, summary.MetodoPago.ToString(),
        OriginText(summary.OrigenFondos), summary.ResponsableOrigen ?? "-", CashEffectText(summary.OrigenFondos, summary.CajaId),
        summary.Referencia, summary.MontoCentavos, summary.SaldoAnteriorCentavos,
        summary.SaldoNuevoCentavos, summary.Observaciones);

    public static SupplierPaymentReceiptViewModel FromHistory(SupplierPaymentHistoryEntry entry) => new(
        entry.Codigo, entry.Tipo, entry.Fecha, $"{entry.CodigoProveedor} - {entry.Proveedor}",
        entry.CodigoCuenta, entry.CodigoCompra, entry.Usuario, entry.MetodoPago.ToString(),
        OriginText(entry.OrigenFondos), entry.ResponsableOrigen ?? "-", CashEffectText(entry.OrigenFondos, entry.CajaId),
        entry.Referencia, entry.MontoCentavos, entry.SaldoAnteriorCentavos,
        entry.SaldoNuevoCentavos, entry.Observaciones);

    public string SuggestedPdfFileName => $"Pago_proveedor_{Sanitize(Codigo)}_{SaleReceiptViewModel.ToLocal(Fecha):yyyyMMdd_HHmm}.pdf";

    public IReadOnlyList<string> BuildReceiptLines() =>
    [
        UITheme.BusinessName,
        "Comprobante interno de pago a proveedor",
        string.Empty,
        $"Codigo: {Codigo}",
        $"Tipo: {Tipo}",
        $"Fecha y hora: {SaleReceiptViewModel.ToLocal(Fecha):yyyy-MM-dd HH:mm}",
        $"Proveedor: {Proveedor}",
        $"Compra: {CodigoCompra}",
        $"Cuenta por pagar: {CodigoCuenta}",
        $"Usuario: {Usuario}",
        string.Empty,
        $"Metodo: {Metodo}",
        $"Origen: {Origen}",
        $"Responsable/fuente: {Responsable}",
        $"Efecto en caja: {EfectoCaja}",
        $"Referencia: {Referencia ?? "-"}",
        $"Monto: {SaleReceiptViewModel.FormatDollars(MontoCentavos)}",
        $"Saldo anterior: {SaleReceiptViewModel.FormatDollars(SaldoAnteriorCentavos)}",
        $"Saldo restante: {SaleReceiptViewModel.FormatDollars(SaldoNuevoCentavos)}",
        $"Observaciones: {Observaciones ?? "-"}",
        string.Empty,
        "Este documento es un comprobante interno de pago.",
        "No es factura fiscal."
    ];

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    private static string OriginText(OrigenFondos? origin) => origin?.ToString() ?? "No especificado (histórico)";

    private static string CashEffectText(OrigenFondos? origin, int? cajaId) =>
        origin is null ? "No especificado (histórico)" : cajaId.HasValue ? "Reduce caja física" : "No afecta caja física";
}
