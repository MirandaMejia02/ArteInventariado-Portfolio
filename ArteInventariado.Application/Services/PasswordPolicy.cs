namespace ArteInventariado.Application.Services;

public static class PasswordPolicy
{
    public const int MinimumLength = 8;

    public static void EnsureValid(string? password, string parameterName = "password")
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("La contrasena es obligatoria.", parameterName);
        }

        if (password.Length < MinimumLength)
        {
            throw new ArgumentException(
                $"La contrasena debe tener al menos {MinimumLength} caracteres.",
                parameterName);
        }
    }
}
