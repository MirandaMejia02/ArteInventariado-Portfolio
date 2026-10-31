using System.Security.Cryptography;
using System.Text;

namespace ArteInventariado.Infrastructure.Services;

internal static class OperationalOperationIds
{
    internal static Guid Derive(Guid operationId, string scope, int discriminator = 0)
    {
        if (operationId == Guid.Empty)
            throw new ArgumentException("El identificador de operacion es obligatorio.", nameof(operationId));

        var scopeBytes = Encoding.UTF8.GetBytes(scope);
        var payload = new byte[16 + sizeof(int) + scopeBytes.Length];
        operationId.TryWriteBytes(payload);
        BitConverter.GetBytes(discriminator).CopyTo(payload, 16);
        scopeBytes.CopyTo(payload, 16 + sizeof(int));
        return new Guid(SHA256.HashData(payload)[..16]);
    }
}
