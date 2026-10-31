using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class CategoryService : ICategoryService
{
    public const string DefaultCategoryName = "Sin categoria";

    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly IAuditService _auditService;
    private readonly ICurrentSession _currentSession;

    [ActivatorUtilitiesConstructor]
    public CategoryService(
        IDbContextFactory<AppDbContext> contextFactory,
        IAuditService auditService,
        ICurrentSession currentSession)
    {
        _contextFactory = contextFactory;
        _auditService = auditService;
        _currentSession = currentSession;
    }

    internal CategoryService(AppDbContext context, IAuditService auditService, ICurrentSession currentSession)
    {
        _legacyContext = context;
        _auditService = auditService;
        _currentSession = currentSession;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para la categoria.");

    public async Task<IReadOnlyList<CategorySummary>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        var categories = await Context.Categorias
            .AsNoTracking()
            .OrderBy(c => c.Nombre)
            .Select(c => new CategorySummary(c.CategoriaId, c.Nombre, c.Descripcion, c.Activa))
            .ToListAsync(cancellationToken);
        return EnsureCategoryCollection(categories);
    }

    public async Task<IReadOnlyList<CategorySummary>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        var categories = await Context.Categorias
            .AsNoTracking()
            .Where(c => c.Activa)
            .OrderBy(c => c.Nombre)
            .Select(c => new CategorySummary(c.CategoriaId, c.Nombre, c.Descripcion, c.Activa))
            .ToListAsync(cancellationToken);
        return EnsureCategoryCollection(categories);
    }

    public async Task<CategorySummary> GetDefaultCategoryAsync(CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        var categoria = await Context.Categorias
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Nombre == DefaultCategoryName, cancellationToken)
            ?? throw new InvalidOperationException("La categoria predeterminada no esta inicializada.");
        return ToSummary(categoria);
    }

    public async Task<CategorySummary> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        ValidarNombre(request.Nombre);
        var nombre = request.Nombre.Trim();

        await EnsureNombreUnicoAsync(nombre, null, cancellationToken);

        var categoria = new Categoria
        {
            Nombre = nombre,
            Descripcion = NormalizarTextoOpcional(request.Descripcion),
            Activa = true,
            FechaCreacion = DateTime.UtcNow
        };

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        Context.Categorias.Add(categoria);
        await Context.SaveChangesAsync(cancellationToken);

        await RegistrarAuditoriaAsync(
            TipoBitacora.CONFIGURACION_MODIFICADA,
            categoria.CategoriaId,
            $"Categoria '{categoria.Nombre}' creada.",
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return ToSummary(categoria);
    }

    public async Task<CategorySummary> UpdateAsync(UpdateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        ValidarNombre(request.Nombre);

        var categoria = await Context.Categorias
            .FirstOrDefaultAsync(c => c.CategoriaId == request.CategoriaId, cancellationToken);

        if (categoria is null)
        {
            throw new InvalidOperationException("La categoria no existe.");
        }

        var nombre = request.Nombre.Trim();
        if (categoria.Nombre == DefaultCategoryName && nombre != DefaultCategoryName)
        {
            throw new InvalidOperationException("La categoria por defecto no se puede renombrar.");
        }

        await EnsureNombreUnicoAsync(nombre, categoria.CategoriaId, cancellationToken);

        categoria.Nombre = nombre;
        categoria.Descripcion = NormalizarTextoOpcional(request.Descripcion);
        categoria.FechaModificacion = DateTime.UtcNow;

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        await Context.SaveChangesAsync(cancellationToken);

        await RegistrarAuditoriaAsync(
            TipoBitacora.CONFIGURACION_MODIFICADA,
            categoria.CategoriaId,
            $"Categoria '{categoria.Nombre}' editada.",
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return ToSummary(categoria);
    }

    public async Task SetActiveAsync(int categoriaId, bool activa, CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        var categoria = await Context.Categorias
            .FirstOrDefaultAsync(c => c.CategoriaId == categoriaId, cancellationToken);

        if (categoria is null)
        {
            throw new InvalidOperationException("La categoria no existe.");
        }

        if (categoria.Nombre == DefaultCategoryName && !activa)
        {
            throw new InvalidOperationException("La categoria por defecto no se puede desactivar.");
        }

        if (categoria.Activa == activa)
        {
            return;
        }

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        categoria.Activa = activa;
        categoria.FechaModificacion = DateTime.UtcNow;
        await Context.SaveChangesAsync(cancellationToken);

        await RegistrarAuditoriaAsync(
            TipoBitacora.CONFIGURACION_MODIFICADA,
            categoria.CategoriaId,
            activa
                ? $"Categoria '{categoria.Nombre}' activada."
                : $"Categoria '{categoria.Nombre}' desactivada.",
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(int categoriaId, CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        var categoria = await Context.Categorias
            .FirstOrDefaultAsync(item => item.CategoriaId == categoriaId, cancellationToken);

        if (categoria is null)
        {
            throw new InvalidOperationException("La categoria no existe.");
        }

        if (string.Equals(categoria.Nombre.Trim(), DefaultCategoryName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("La categoria 'Sin categoria' esta protegida y no se puede eliminar.");
        }

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        var hasProducts = await Context.Productos.AnyAsync(
            product => product.CategoriaId == categoriaId,
            cancellationToken);
        var hasSubcategories = await Context.Subcategorias.AnyAsync(
            subcategory => subcategory.CategoriaId == categoriaId,
            cancellationToken);
        var hasSalesHistory = await Context.DetalleVentas.AnyAsync(
                detail => detail.CategoriaIdSnapshot == categoriaId,
                cancellationToken) ||
            await Context.DetallesCambioVenta.AnyAsync(
                detail => detail.CategoriaIdSnapshot == categoriaId,
                cancellationToken);

        if (hasProducts || hasSubcategories || hasSalesHistory)
        {
            throw new InvalidOperationException(
                "No se puede eliminar porque la categoria tiene productos, subcategorias o historial comercial asociado. " +
                "Desactivala para conservar sus referencias e historicos.");
        }

        Context.Categorias.Remove(categoria);
        await Context.SaveChangesAsync(cancellationToken);
        await RegistrarAuditoriaAsync(
            TipoBitacora.CONFIGURACION_MODIFICADA,
            categoriaId,
            $"Categoria '{categoria.Nombre}' eliminada.",
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task EnsureNombreUnicoAsync(string nombre, int? categoriaIdActual, CancellationToken cancellationToken)
    {
        var nombreNormalizado = nombre.ToUpper();
        var existe = await Context.Categorias
            .AnyAsync(
                c => c.Nombre.ToUpper() == nombreNormalizado &&
                    (!categoriaIdActual.HasValue || c.CategoriaId != categoriaIdActual.Value),
                cancellationToken);

        if (existe)
        {
            throw new InvalidOperationException("Ya existe una categoria con ese nombre.");
        }
    }

    private async Task RegistrarAuditoriaAsync(
        TipoBitacora tipoAccion,
        int categoriaId,
        string descripcion,
        CancellationToken cancellationToken)
    {
        await _auditService.RegistrarAsync(
            new AuditEntryRequest(
                GetUsuarioActualId(),
                tipoAccion,
                "categorias",
                categoriaId,
                descripcion),
            cancellationToken);
    }

    private static void ValidarNombre(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            throw new ArgumentException("El nombre de la categoria es obligatorio.", nameof(nombre));
        }
    }

    private int GetUsuarioActualId()
    {
        return _currentSession.UsuarioActual?.UsuarioId
            ?? throw new InvalidOperationException("Debe existir una sesion activa para administrar categorias.");
    }

    private static string? NormalizarTextoOpcional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    internal static IReadOnlyList<CategorySummary> EnsureCategoryCollection(
        IReadOnlyList<CategorySummary>? categories)
    {
        return categories ?? [];
    }

    private static CategorySummary ToSummary(Categoria categoria)
    {
        return new CategorySummary(categoria.CategoriaId, categoria.Nombre, categoria.Descripcion, categoria.Activa);
    }
}
