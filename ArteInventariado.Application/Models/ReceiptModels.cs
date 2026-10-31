using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

public sealed record ReceiptBusinessData(
    string Name,
    string? Address = null,
    string? Phone = null,
    string? Email = null);

public sealed record ReceiptData(
    ReceiptBusinessData Business,
    int SaleId,
    string SaleNumber,
    DateTime SoldAtUtc,
    string Cashier,
    string Customer,
    long SubtotalCents,
    long DiscountCents,
    long TotalCents,
    long PaidCents,
    long ChangeCents,
    IReadOnlyList<ReceiptLineData> Lines,
    IReadOnlyList<ReceiptPaymentData> Payments,
    ReceiptCreditData? Credit = null);

public sealed record ReceiptLineData(
    string ProductName,
    string HistoricalCode,
    string? HistoricalSku,
    string? HistoricalVariant,
    int Quantity,
    long UnitPriceCents,
    long SubtotalCents,
    long DiscountCents,
    long TotalCents);

public sealed record ReceiptPaymentData(
    MetodoPago Method,
    long AmountCents,
    string? Reference);

public sealed record ReceiptCreditData(
    string AccountCode,
    long FinancedPrincipalCents,
    decimal InterestPercent,
    long InterestCents,
    long AccountTotalCents,
    long InitialBalanceCents,
    DateTime DueAtUtc);

public static class ReceiptDataFactory
{
    public static ReceiptData FromSaleResult(
        SaleResult sale,
        ReceiptBusinessData business,
        string cashier)
    {
        ArgumentNullException.ThrowIfNull(sale);
        return new ReceiptData(
            business,
            sale.VentaId,
            sale.NumeroVenta,
            sale.FechaVenta,
            cashier,
            sale.ClienteNombreSnapshot,
            sale.SubtotalCentavos,
            sale.DescuentoCentavos,
            sale.TotalCentavos,
            sale.TotalPagadoCentavos,
            sale.CambioCentavos,
            sale.Lines.Select(line => new ReceiptLineData(
                line.ProductoNombre,
                line.CodigoInterno,
                line.CodigoSku,
                line.NombreVariante,
                line.Cantidad,
                line.PrecioUnitarioCentavos,
                line.SubtotalCentavos,
                line.DescuentoCentavos,
                line.TotalCentavos)).ToList(),
            sale.Payments.Select(payment => new ReceiptPaymentData(
                payment.MetodoPago,
                payment.MontoCentavos,
                payment.Referencia)).ToList(),
            sale.Credit is null
                ? null
                : new ReceiptCreditData(
                    sale.Credit.CodigoCuenta,
                    sale.Credit.CapitalFinanciadoCentavos,
                    sale.Credit.InteresPorcentaje,
                    sale.Credit.InteresCalculadoCentavos,
                    sale.Credit.TotalCuentaCentavos,
                    sale.Credit.SaldoInicialCentavos,
                    sale.Credit.FechaVencimiento));
    }

    public static ReceiptData FromSalesHistory(
        SalesHistoryDetail sale,
        ReceiptBusinessData business)
    {
        ArgumentNullException.ThrowIfNull(sale);
        return new ReceiptData(
            business,
            sale.VentaId,
            sale.NumeroVenta,
            sale.FechaVenta,
            sale.Usuario,
            sale.Cliente,
            sale.SubtotalCentavos,
            sale.DescuentoCentavos,
            sale.TotalCentavos,
            sale.TotalPagadoCentavos,
            sale.CambioCentavos,
            sale.Lines.Select(line => new ReceiptLineData(
                ProductNameWithoutVariant(line.Producto, line.ProductoCodigoHistorico ?? line.Codigo),
                line.ProductoCodigoHistorico ?? line.Codigo,
                line.CodigoSkuHistorico,
                line.VarianteHistorica ?? HistoricalVariant(line.Producto),
                line.Cantidad,
                line.PrecioUnitarioCentavos,
                line.SubtotalCentavos,
                line.DescuentoCentavos,
                line.TotalCentavos)).ToList(),
            sale.Payments.Select(payment => new ReceiptPaymentData(
                payment.MetodoPago,
                payment.MontoCentavos,
                payment.Referencia)).ToList(),
            sale.Credit is null
                ? null
                : new ReceiptCreditData(
                    sale.Credit.CodigoCuenta,
                    sale.Credit.CapitalFinanciadoCentavos,
                    sale.Credit.InteresPorcentaje,
                    sale.Credit.InteresCalculadoCentavos,
                    sale.Credit.TotalCuentaCentavos,
                    sale.Credit.SaldoActualCentavos,
                    sale.Credit.FechaVencimiento));
    }

    private static string ProductNameWithoutVariant(string historicalName, string code)
    {
        var separator = historicalName.IndexOf(" · ", StringComparison.Ordinal);
        var name = separator < 0 ? historicalName : historicalName[..separator];
        return string.IsNullOrWhiteSpace(name) ? code : name;
    }

    private static string? HistoricalVariant(string historicalName)
    {
        var separator = historicalName.IndexOf(" · ", StringComparison.Ordinal);
        return separator < 0 ? null : historicalName[(separator + 3)..];
    }
}

public sealed record ReceiptRenderOptions(int CharactersPerLine = 42)
{
    public int SafeCharactersPerLine => Math.Clamp(CharactersPerLine, 32, 48);
}

public static class ReceiptTextRenderer
{
    public static IReadOnlyList<string> Render(ReceiptData receipt, ReceiptRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var width = (options ?? new ReceiptRenderOptions()).SafeCharactersPerLine;
        var lines = new List<string>();
        AddCentered(lines, receipt.Business.Name, width);
        AddOptional(lines, receipt.Business.Address, width);
        AddOptional(lines, receipt.Business.Phone, width);
        AddOptional(lines, receipt.Business.Email, width);
        lines.Add(Center("TICKET DE VENTA", width));
        lines.Add(new string('-', width));
        lines.Add($"Venta: {receipt.SaleNumber}");
        lines.Add($"Fecha: {ToLocal(receipt.SoldAtUtc):yyyy-MM-dd HH:mm}");
        lines.Add($"Cajero: {receipt.Cashier}");
        lines.Add($"Cliente: {receipt.Customer}");
        lines.Add(new string('-', width));

        foreach (var item in receipt.Lines)
        {
            var identity = item.HistoricalVariant is null
                ? $"{item.ProductName} [{item.HistoricalSku ?? item.HistoricalCode}]"
                : $"{item.ProductName} · {item.HistoricalVariant} [{item.HistoricalSku ?? item.HistoricalCode}]";
            lines.AddRange(Wrap(identity, width));
            lines.Add($"  {item.Quantity} x {Money(item.UnitPriceCents)} = {Money(item.SubtotalCents)}");
            if (item.DiscountCents > 0)
                lines.Add($"  Descuento: {Money(item.DiscountCents)}");
            lines.Add($"  Total linea: {Money(item.TotalCents)}");
        }

        lines.Add(new string('-', width));
        lines.Add(AmountLine("Subtotal", receipt.SubtotalCents, width));
        lines.Add(AmountLine("Descuentos", receipt.DiscountCents, width));
        lines.Add(AmountLine("TOTAL", receipt.TotalCents, width));
        lines.Add(new string('-', width));
        foreach (var payment in receipt.Payments)
        {
            lines.Add(AmountLine(payment.Method.ToString(), payment.AmountCents, width));
            if (!string.IsNullOrWhiteSpace(payment.Reference))
                lines.AddRange(Wrap($"Ref. {payment.Reference.Trim()}", width).Select(line => "  " + line));
        }
        lines.Add(AmountLine("Pagado", receipt.PaidCents, width));
        lines.Add(AmountLine("Vuelto", receipt.ChangeCents, width));

        if (receipt.Credit is not null)
        {
            lines.Add(new string('-', width));
            lines.Add("VENTA A CREDITO");
            lines.Add($"Cuenta: {receipt.Credit.AccountCode}");
            lines.Add(AmountLine("Capital", receipt.Credit.FinancedPrincipalCents, width));
            lines.Add(AmountLine("Interes", receipt.Credit.InterestCents, width));
            lines.Add(AmountLine("Total cuenta", receipt.Credit.AccountTotalCents, width));
            lines.Add(AmountLine("Saldo inicial", receipt.Credit.InitialBalanceCents, width));
            lines.Add($"Vencimiento: {ToLocal(receipt.Credit.DueAtUtc):yyyy-MM-dd}");
        }

        lines.Add(new string('-', width));
        lines.Add(Center("Gracias por su compra.", width));
        lines.Add(new string('-', width));
        lines.AddRange(Wrap(
            "Cambios y devoluciones sujetos a revisión y condiciones de la tienda. Conserve este recibo.",
            width));
        return lines;
    }

    public static string RenderText(ReceiptData receipt, ReceiptRenderOptions? options = null) =>
        string.Join(Environment.NewLine, Render(receipt, options));

    public static string Money(long cents) => "$" + (cents / 100m).ToString("0.00");

    private static string AmountLine(string label, long amount, int width)
    {
        var value = Money(amount);
        var spacing = Math.Max(1, width - label.Length - value.Length);
        return label + new string(' ', spacing) + value;
    }

    private static void AddCentered(List<string> lines, string value, int width)
    {
        foreach (var line in Wrap(value, width)) lines.Add(Center(line, width));
    }

    private static void AddOptional(List<string> lines, string? value, int width)
    {
        if (!string.IsNullOrWhiteSpace(value)) AddCentered(lines, value.Trim(), width);
    }

    private static string Center(string value, int width) =>
        value.Length >= width ? value : new string(' ', (width - value.Length) / 2) + value;

    private static IEnumerable<string> Wrap(string value, int width)
    {
        var remaining = value.Trim();
        while (remaining.Length > width)
        {
            var split = remaining.LastIndexOf(' ', width);
            if (split <= 0) split = width;
            yield return remaining[..split].TrimEnd();
            remaining = remaining[split..].TrimStart();
        }
        if (remaining.Length > 0) yield return remaining;
    }

    private static DateTime ToLocal(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value,
        DateTimeKind.Utc => value.ToLocalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime()
    };
}
