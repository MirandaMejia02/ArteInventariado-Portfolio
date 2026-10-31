namespace ArteInventariado.Tests;

/// <summary>
/// Produces per-test credentials without putting a reusable password in the
/// repository. These values are test data only and are never used by the
/// application bootstrap.
/// </summary>
internal static class TestCredentials
{
    public static string Password(string purpose = "test") =>
        $"{purpose}-{Guid.NewGuid():N}-Aa1!";
}
