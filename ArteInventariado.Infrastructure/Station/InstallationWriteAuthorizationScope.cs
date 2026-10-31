namespace ArteInventariado.Infrastructure.Station;

internal sealed class InstallationWriteAuthorizationScope : IDisposable
{
    private static readonly AsyncLocal<int> Depth = new();
    private bool _disposed;

    private InstallationWriteAuthorizationScope()
    {
        Depth.Value++;
    }

    public static bool IsAuthorized => Depth.Value > 0;

    public static InstallationWriteAuthorizationScope Begin() => new();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Depth.Value = Math.Max(0, Depth.Value - 1);
        _disposed = true;
    }
}
