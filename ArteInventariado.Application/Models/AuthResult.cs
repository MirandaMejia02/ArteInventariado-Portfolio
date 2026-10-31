namespace ArteInventariado.Application.Models;

public sealed record AuthResult(
    bool Exitoso,
    UsuarioSesion? Usuario,
    string Mensaje)
{
    public static AuthResult Ok(UsuarioSesion usuario)
    {
        return new AuthResult(true, usuario, "Inicio de sesion correcto.");
    }

    public static AuthResult Fallo(string mensaje)
    {
        return new AuthResult(false, null, mensaje);
    }
}
