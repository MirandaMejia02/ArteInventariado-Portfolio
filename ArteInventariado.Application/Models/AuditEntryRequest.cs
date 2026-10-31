using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

public sealed record AuditEntryRequest(
    int? UsuarioId,
    TipoBitacora TipoAccion,
    string Entidad,
    int? EntidadId,
    string Descripcion,
    string? DatosAntes = null,
    string? DatosDespues = null);
