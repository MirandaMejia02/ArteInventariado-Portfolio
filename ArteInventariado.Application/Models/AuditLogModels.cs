using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

public sealed record AuditLogFilter(
    DateOnly? FechaDesde = null,
    DateOnly? FechaHasta = null,
    int? UsuarioId = null,
    TipoBitacora? TipoAccion = null,
    string? TextoBusqueda = null);

public sealed record AuditLogEntry(
    int BitacoraId,
    DateTime FechaAccion,
    string Usuario,
    TipoBitacora TipoAccion,
    string Entidad,
    string Descripcion);
