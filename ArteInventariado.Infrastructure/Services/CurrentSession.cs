using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Infrastructure.Services;

public class CurrentSession : ICurrentSession
{
    public UsuarioSesion? UsuarioActual { get; private set; }

    public bool EstaAutenticado => UsuarioActual is not null;

    public bool EsAdmin => TieneRol(RolUsuario.ADMIN);

    public bool TieneRol(RolUsuario rol)
    {
        return UsuarioActual?.Rol == rol;
    }

    public void IniciarSesion(UsuarioSesion usuario)
    {
        UsuarioActual = usuario;
    }

    public void CerrarSesion()
    {
        UsuarioActual = null;
    }
}
