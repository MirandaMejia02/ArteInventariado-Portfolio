using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

public sealed record UpdateUserRequest(
    int UsuarioId,
    string NombreCompleto,
    RolUsuario Rol);
