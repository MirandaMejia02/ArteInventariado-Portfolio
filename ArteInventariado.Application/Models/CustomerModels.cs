using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

public sealed record CustomerFilter(
    string? Search = null,
    EstadoCliente? Estado = null,
    int Page = 1,
    int PageSize = 100);

public sealed record CustomerSummary(
    int ClienteId,
    string CodigoCliente,
    string NombreCompleto,
    string? TelefonoCelular,
    string? DocumentoIdentidad,
    EstadoCliente Estado,
    DateTime? UltimaCompra,
    int TotalCompras);

public sealed record CustomerDetail(
    int ClienteId,
    string CodigoCliente,
    string NombreCompleto,
    string? TelefonoCelular,
    string? WhatsApp,
    string? CorreoElectronico,
    string? Direccion,
    string? DocumentoIdentidad,
    string? Notas,
    EstadoCliente Estado,
    DateTime FechaCreacion,
    DateTime FechaActualizacion,
    string UsuarioCreacion,
    string? UsuarioActualizacion);

public sealed record CustomerSaveRequest(
    int? ClienteId,
    string NombreCompleto,
    string? TelefonoCelular = null,
    string? WhatsApp = null,
    string? CorreoElectronico = null,
    string? Direccion = null,
    string? DocumentoIdentidad = null,
    string? Notas = null,
    bool ConfirmDuplicateWarning = false);

public sealed record CustomerStateChangeRequest(
    int ClienteId,
    EstadoCliente Estado,
    string Motivo);

public sealed record CustomerDuplicateCandidate(
    int ClienteId,
    string CodigoCliente,
    string NombreCompleto,
    string? TelefonoCelular,
    string? DocumentoIdentidad,
    EstadoCliente Estado,
    CustomerDuplicateKind Kind,
    bool Blocking);

public sealed record CustomerDuplicateCheckResult(
    IReadOnlyList<CustomerDuplicateCandidate> Candidates)
{
    public bool HasBlockingDuplicate => Candidates.Any(c => c.Blocking);
    public bool HasWarnings => Candidates.Any(c => !c.Blocking);
}

public enum CustomerDuplicateKind
{
    Documento,
    Telefono,
    Correo,
    NombreParecido
}

public sealed record CustomerPurchaseHistoryRow(
    int VentaId,
    DateTime FechaVenta,
    string NumeroVenta,
    string Usuario,
    long TotalCentavos,
    string MetodosPago,
    EstadoVenta Estado);

public sealed record CustomerSaleSelection(
    int ClienteId,
    string CodigoCliente,
    string NombreCompleto,
    bool Activo);
