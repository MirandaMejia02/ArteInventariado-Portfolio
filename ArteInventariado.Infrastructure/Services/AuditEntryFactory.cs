using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;

namespace ArteInventariado.Infrastructure.Services;

internal static class AuditEntryFactory
{
    public static Bitacora Create(AuditEntryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Entidad))
        {
            throw new ArgumentException("La entidad de bitacora es obligatoria.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Descripcion))
        {
            throw new ArgumentException("La descripcion de bitacora es obligatoria.", nameof(request));
        }

        return new Bitacora
        {
            BitacoraId = 0,
            UsuarioId = request.UsuarioId,
            TipoAccion = request.TipoAccion,
            Entidad = request.Entidad.Trim(),
            EntidadId = request.EntidadId,
            Descripcion = request.Descripcion.Trim(),
            DatosAntes = string.IsNullOrWhiteSpace(request.DatosAntes) ? null : request.DatosAntes,
            DatosDespues = string.IsNullOrWhiteSpace(request.DatosDespues) ? null : request.DatosDespues,
            FechaAccion = DateTime.UtcNow
        };
    }
}
