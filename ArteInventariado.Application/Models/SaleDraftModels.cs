namespace ArteInventariado.Application.Models;

/// <summary>
/// Una línea de venta que vive únicamente mientras el proceso POS está abierto.
/// No representa una entidad persistida ni reserva inventario.
/// </summary>
public sealed class SaleDraftLine
{
    public SaleDraftLine(PosSkuSaleItem product)
    {
        Product = product ?? throw new ArgumentNullException(nameof(product));
    }

    public PosSkuSaleItem Product { get; set; }

    public int Quantity { get; set; } = 1;

    public long DiscountCentavos { get; set; }

    public string? DiscountReason { get; set; }

    public long SubtotalCentavos => Product.PrecioVentaEfectivoCentavos * Quantity;

    public long TotalCentavos => Math.Max(0, SubtotalCentavos - DiscountCentavos);
}

/// <summary>
/// Estado de checkout asociado a un draft. Se mantiene en memoria y se reinicia
/// al cerrar la aplicación; no se escribe en SQLite.
/// </summary>
public sealed class SaleDraftCheckoutState
{
    public long GeneralDiscountCentavos { get; set; }

    public string? GeneralDiscountReason { get; set; }

    public string CurrentPaymentMethod { get; set; } = "EFECTIVO";

    public long CashReceivedCentavos { get; set; }

    public long AcceptedCashCentavos { get; set; }

    public long AcceptedTransferCentavos { get; set; }

    public string AcceptedTransferReference { get; set; } = string.Empty;

    public long AcceptedBitcoinCentavos { get; set; }

    public string AcceptedBitcoinReference { get; set; } = string.Empty;

    public bool CashReceivedEditedManually { get; set; }

    public CreditSaleRequest? PendingCreditRequest { get; set; }

    public void Reset()
    {
        GeneralDiscountCentavos = 0;
        GeneralDiscountReason = null;
        CurrentPaymentMethod = "EFECTIVO";
        CashReceivedCentavos = 0;
        AcceptedCashCentavos = 0;
        AcceptedTransferCentavos = 0;
        AcceptedTransferReference = string.Empty;
        AcceptedBitcoinCentavos = 0;
        AcceptedBitcoinReference = string.Empty;
        CashReceivedEditedManually = false;
        PendingCreditRequest = null;
    }
}

/// <summary>
/// Borrador de venta no persistido. Su identidad es interna al proceso POS.
/// </summary>
public sealed class SaleDraft
{
    public SaleDraft(string title, DateTimeOffset? createdAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("El título del draft es obligatorio.", nameof(title));
        }

        Title = title.Trim();
        CreatedAtUtc = (createdAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
    }

    public Guid Id { get; } = Guid.NewGuid();

    public string Title { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public List<SaleDraftLine> Lines { get; } = [];

    public int? SelectedCustomerId { get; set; }

    public string SelectedCustomerName { get; set; } = "Consumidor final";

    public SaleDraftCheckoutState Checkout { get; } = new();

    public bool IsEmpty => Lines.Count == 0;

    public void Reset()
    {
        Lines.Clear();
        SelectedCustomerId = null;
        SelectedCustomerName = "Consumidor final";
        Checkout.Reset();
    }
}
