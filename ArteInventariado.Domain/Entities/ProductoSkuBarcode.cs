namespace ArteInventariado.Domain.Entities;

using ArteInventariado.Domain.Enums;

public sealed class ProductoSkuBarcode
{
    public int ProductoSkuBarcodeId { get; set; }

    public int ProductoSkuId { get; set; }

    public ProductoSku ProductoSku { get; set; } = null!;

    public string Codigo { get; set; } = string.Empty;

    public BarcodeSymbology Symbology { get; set; } = BarcodeSymbology.Code128;

    public bool Activo { get; set; } = true;

    public bool EsPrincipal { get; set; }
}
