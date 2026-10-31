using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Data;

internal sealed class AppDbContextLease : IAsyncDisposable
{
    private static readonly AsyncLocal<AppDbContext?> AmbientContext = new();

    private readonly bool _ownsContext;
    private readonly AppDbContext? _previousContext;
    private readonly bool _setsAmbientContext;

    private AppDbContextLease(
        AppDbContext context,
        bool ownsContext,
        AppDbContext? previousContext,
        bool setsAmbientContext)
    {
        Context = context;
        _ownsContext = ownsContext;
        _previousContext = previousContext;
        _setsAmbientContext = setsAmbientContext;
    }

    public AppDbContext Context { get; }

    public static ValueTask<AppDbContextLease> CreateAsync(
        IDbContextFactory<AppDbContext>? factory,
        AppDbContext? legacyContext,
        CancellationToken cancellationToken)
    {
        // Keep ambient assignment synchronous: changing AsyncLocal after an await would not flow
        // back to the caller, so nested audit work could open a second SQLite connection.
        cancellationToken.ThrowIfCancellationRequested();

        if (AmbientContext.Value is { } ambientContext)
        {
            return ValueTask.FromResult(new AppDbContextLease(
                ambientContext,
                ownsContext: false,
                previousContext: ambientContext,
                setsAmbientContext: false));
        }

        var context = factory is not null
            ? factory.CreateDbContext()
            : legacyContext ?? throw new InvalidOperationException("No existe un contexto de datos disponible.");

        var previousContext = AmbientContext.Value;
        AmbientContext.Value = context;

        return ValueTask.FromResult(new AppDbContextLease(
            context,
            ownsContext: factory is not null,
            previousContext,
            setsAmbientContext: true));
    }

    public static AppDbContext? Current => AmbientContext.Value;

    public ValueTask DisposeAsync()
    {
        // Restore the ambient value before returning for the same execution-context reason.
        if (_setsAmbientContext)
        {
            AmbientContext.Value = _previousContext;
        }

        return _ownsContext
            ? Context.DisposeAsync()
            : ValueTask.CompletedTask;
    }
}
