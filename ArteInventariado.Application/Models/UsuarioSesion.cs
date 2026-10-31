using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

public sealed record UsuarioSesion(
    int UsuarioId,
    string NombreCompleto,
    string NombreUsuario,
    RolUsuario Rol);
