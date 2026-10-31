namespace ArteInventariado.Domain.Entities;

public class ProductoAsset
{
    public int ProductoAssetId { get; set; }
    public int ProductoId { get; set; }
    public Producto Producto { get; set; } = null!;
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
