using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public sealed record SaleReceiptViewModel(
    int VentaId,
    string NumeroVenta,
    DateTime FechaVenta,
    string Empleado,
    string Cliente,
    long SubtotalCentavos,
    long DescuentoCentavos,
    long TotalCentavos,
    long TotalPagadoCentavos,
    long CambioCentavos,
    IReadOnlyList<SaleReceiptLine> Lines,
    IReadOnlyList<SaleReceiptPayment> Payments,
    SaleReceiptCredit? Credit = null,
    ReceiptBusinessData? Business = null)
{
    public static SaleReceiptViewModel FromSaleResult(
        SaleResult result,
        string empleado,
        ReceiptBusinessData? business = null) =>
        FromReceiptData(ReceiptDataFactory.FromSaleResult(
            result,
            business ?? new ReceiptBusinessData(UITheme.BusinessName),
            empleado));

    public static SaleReceiptViewModel FromSalesHistoryDetail(
        SalesHistoryDetail detail,
        ReceiptBusinessData? business = null) =>
        FromReceiptData(ReceiptDataFactory.FromSalesHistory(
            detail,
            business ?? new ReceiptBusinessData(UITheme.BusinessName)));

    public static SaleReceiptViewModel FromReceiptData(ReceiptData receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return new SaleReceiptViewModel(
            receipt.SaleId,
            receipt.SaleNumber,
            receipt.SoldAtUtc,
            receipt.Cashier,
            receipt.Customer,
            receipt.SubtotalCents,
            receipt.DiscountCents,
            receipt.TotalCents,
            receipt.PaidCents,
            receipt.ChangeCents,
            receipt.Lines.Select(line => new SaleReceiptLine(
                string.IsNullOrWhiteSpace(line.HistoricalVariant)
                    ? line.ProductName
                    : $"{line.ProductName} · {line.HistoricalVariant}",
                line.HistoricalSku ?? line.HistoricalCode,
                line.Quantity,
                line.UnitPriceCents,
                line.SubtotalCents,
                line.DiscountCents,
                line.TotalCents)).ToList(),
            receipt.Payments.Select(payment => new SaleReceiptPayment(
                payment.Method,
                payment.AmountCents,
                payment.Reference)).ToList(),
            receipt.Credit is null
                ? null
                : new SaleReceiptCredit(
                    receipt.Credit.AccountCode,
                    receipt.Credit.FinancedPrincipalCents,
                    receipt.Credit.InterestPercent,
                    receipt.Credit.InterestCents,
                    receipt.Credit.AccountTotalCents,
                    receipt.Credit.InitialBalanceCents,
                    receipt.Credit.DueAtUtc),
            receipt.Business);
    }

    public ReceiptData ToReceiptData() => new(
        Business ?? new ReceiptBusinessData(UITheme.BusinessName),
        VentaId,
        NumeroVenta,
        FechaVenta,
        Empleado,
        Cliente,
        SubtotalCentavos,
        DescuentoCentavos,
        TotalCentavos,
        TotalPagadoCentavos,
        CambioCentavos,
        Lines.Select(line => new ReceiptLineData(
            ProductNameWithoutVariant(line.Producto),
            line.Codigo,
            line.Codigo,
            VariantFromName(line.Producto),
            line.Cantidad,
            line.PrecioUnitarioCentavos,
            line.SubtotalCentavos,
            line.DescuentoCentavos,
            line.TotalCentavos)).ToList(),
        Payments.Select(payment => new ReceiptPaymentData(
            payment.MetodoPago,
            payment.MontoCentavos,
            payment.Referencia)).ToList(),
        Credit is null
            ? null
            : new ReceiptCreditData(
                Credit.CodigoCuenta,
                Credit.CapitalFinanciadoCentavos,
                Credit.InteresPorcentaje,
                Credit.InteresCalculadoCentavos,
                Credit.TotalCuentaCentavos,
                Credit.SaldoInicialCentavos,
                Credit.FechaVencimiento));

    public string SuggestedPdfFileName => $"{SanitizeFileName(NumeroVenta)}_ticket_venta.pdf";

    public IReadOnlyList<string> BuildTicketLines() => ReceiptTextRenderer.Render(ToReceiptData());

    public string BuildTicketText() => ReceiptTextRenderer.RenderText(ToReceiptData());

    public static DateTime ToLocal(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value,
        DateTimeKind.Utc => value.ToLocalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime()
    };

    public static string FormatDollars(long centavos) => ReceiptTextRenderer.Money(centavos);

    private static string ProductNameWithoutVariant(string value)
    {
        var separator = value.IndexOf(" · ", StringComparison.Ordinal);
        return separator < 0 ? value : value[..separator];
    }

    private static string? VariantFromName(string value)
    {
        var separator = value.IndexOf(" · ", StringComparison.Ordinal);
        return separator < 0 ? null : value[(separator + 3)..];
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}

public sealed record SaleReceiptLine(
    string Producto,
    string Codigo,
    int Cantidad,
    long PrecioUnitarioCentavos,
    long SubtotalCentavos,
    long DescuentoCentavos,
    long TotalCentavos);

public sealed record SaleReceiptPayment(
    MetodoPago MetodoPago,
    long MontoCentavos,
    string? Referencia);

public sealed record SaleReceiptCredit(
    string CodigoCuenta,
    long CapitalFinanciadoCentavos,
    decimal InteresPorcentaje,
    long InteresCalculadoCentavos,
    long TotalCuentaCentavos,
    long SaldoInicialCentavos,
    DateTime FechaVencimiento);
