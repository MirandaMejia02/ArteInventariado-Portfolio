using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Services;

public static class ProtectedAdministrator
{
    public const string UserName = "admin";

    public static bool IsUserName(string? userName) =>
        string.Equals(userName?.Trim(), UserName, StringComparison.OrdinalIgnoreCase);

    public static bool IsProtectedAdministrator(UsuarioSesion? user) =>
        user is { Rol: RolUsuario.ADMIN } && IsUserName(user.NombreUsuario);
}
