using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

public sealed record SupplierFilter(
    string? Search = null,
    TipoProveedor? Tipo = null,
    EstadoProveedor? Estado = null,
    int Page = 1,
    int PageSize = 100);

public sealed record SupplierSummary(
    int ProveedorId,
    string CodigoProveedor,
    TipoProveedor Tipo,
    string NombreRazonSocial,
    string? NombreContacto,
    string? Telefono,
    string? DocumentoIdentidad,
    string? DocumentoFiscal,
    EstadoProveedor Estado,
    int ProductosAsociados,
    DateTime? UltimaCompra);

public sealed record SupplierDetail(
    int ProveedorId,
    string CodigoProveedor,
    TipoProveedor Tipo,
    string NombreRazonSocial,
    string? NombreContacto,
    string? Telefono,
    string? WhatsApp,
    string? Correo,
    string? Direccion,
    string? DocumentoIdentidad,
    string? DocumentoFiscal,
    string? Notas,
    EstadoProveedor Estado,
    DateTime FechaCreacion,
    DateTime FechaActualizacion,
    string UsuarioCreacion,
    string? UsuarioActualizacion,
    string? MotivoDesactivacion,
    DateTime? FechaDesactivacion,
    IReadOnlyList<SupplierProductSummary> Productos,
    IReadOnlyList<SupplierHistoryEntry> Historial);

public sealed record SupplierSaveRequest(
    int? ProveedorId,
    TipoProveedor Tipo,
    string NombreRazonSocial,
    string? NombreContacto = null,
    string? Telefono = null,
    string? WhatsApp = null,
    string? Correo = null,
    string? Direccion = null,
    string? DocumentoIdentidad = null,
    string? DocumentoFiscal = null,
    string? Notas = null,
    bool ConfirmDuplicateWarning = false);

public sealed record SupplierStateChangeRequest(
    int ProveedorId,
    EstadoProveedor Estado,
    string Motivo);

public sealed record SupplierDuplicateCandidate(
    int ProveedorId,
    string CodigoProveedor,
    string NombreRazonSocial,
    string? Telefono,
    string? WhatsApp,
    string? Correo,
    string? DocumentoIdentidad,
    string? DocumentoFiscal,
    EstadoProveedor Estado,
    SupplierDuplicateKind Kind,
    bool Blocking);

public sealed record SupplierDuplicateCheckResult(
    IReadOnlyList<SupplierDuplicateCandidate> Candidates)
{
    public bool HasBlockingDuplicate => Candidates.Any(c => c.Blocking);
    public bool HasWarnings => Candidates.Any(c => !c.Blocking);
}

public enum SupplierDuplicateKind
{
    DocumentoIdentidad,
    DocumentoFiscal,
    Correo,
    Telefono,
    WhatsApp,
    NombreParecido
}

public sealed record SupplierProductSummary(
    int ProductoProveedorId,
    int ProductoId,
    string CodigoProducto,
    string Producto,
    string Categoria,
    string? CodigoProductoProveedor,
    bool EsProveedorPrincipal,
    long? UltimoCostoCentavos,
    DateTime? FechaUltimaCompra,
    bool Activo,
    string? Notas,
    string? Subcategoria = null);

public sealed record SupplierProductAssociationRequest(
    int ProveedorId,
    int ProductoId,
    string? CodigoProductoProveedor = null,
    bool EsProveedorPrincipal = false,
    string? Notas = null);

public sealed record SupplierProductMainRequest(
    int ProductoProveedorId,
    bool EsProveedorPrincipal);

public sealed record SupplierHistoryEntry(
    DateTime Fecha,
    string Tipo,
    string Usuario,
    string Descripcion);
