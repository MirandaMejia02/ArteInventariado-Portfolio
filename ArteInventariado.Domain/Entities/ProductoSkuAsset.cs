namespace ArteInventariado.Domain.Entities;

public class ProductoSkuAsset
{
    public int ProductoSkuAssetId { get; set; }
    public int ProductoSkuId { get; set; }
    public ProductoSku ProductoSku { get; set; } = null!;
    public Guid StorageKey { get; set; }
    public string RutaRelativa { get; set; } = string.Empty;
    public string NombreOriginal { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public int Orden { get; set; }
    public bool EsPrincipal { get; set; }
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
}
