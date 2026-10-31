namespace ArteInventariado.WinForms;

internal static class ControlledShutdownState
{
    private static int _active;

    public static bool IsActive => Volatile.Read(ref _active) == 1;

    public static void Begin() => Interlocked.Exchange(ref _active, 1);
}
