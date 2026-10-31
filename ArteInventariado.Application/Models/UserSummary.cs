using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

public sealed record UserSummary(
    int UsuarioId,
    string NombreCompleto,
    string NombreUsuario,
    RolUsuario Rol,
    bool Activo,
    DateTime FechaCreacion,
    DateTime? UltimoLogin);
