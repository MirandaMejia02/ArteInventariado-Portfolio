using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.Application.Interfaces;

public interface ICurrentSession
{
    UsuarioSesion? UsuarioActual { get; }

    bool EstaAutenticado { get; }

    bool EsAdmin { get; }

    bool TieneRol(RolUsuario rol);

    void IniciarSesion(UsuarioSesion usuario);

    void CerrarSesion();
}
