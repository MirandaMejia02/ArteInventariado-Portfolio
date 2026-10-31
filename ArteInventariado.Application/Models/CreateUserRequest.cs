using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Models;

public sealed record CreateUserRequest(
    string NombreCompleto,
    string NombreUsuario,
    string Password,
    RolUsuario Rol);
