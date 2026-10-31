using System.Linq.Expressions;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public sealed class SubcategoryService : ISubcategoryService
{
    private static readonly string ProtectedCategoryNameNormalized =
        CategoryService.DefaultCategoryName.ToUpperInvariant();

    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly IAuditService _auditService;
    private readonly ICurrentSession _currentSession;

    [ActivatorUtilitiesConstructor]
    public SubcategoryService(
        IDbContextFactory<AppDbContext> contextFactory,
        IAuditService auditService,
        ICurrentSession currentSession)
    {
        _contextFactory = contextFactory;
        _auditService = auditService;
        _currentSession = currentSession;
    }

    // Conservado para fixtures unitarios que comparten una conexion SQLite en memoria.
    internal SubcategoryService(
        AppDbContext context,
        IAuditService auditService,
        ICurrentSession currentSession)
    {
        _legacyContext = context;
        _auditService = auditService;
        _currentSession = currentSession;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para la subcategoria.");

    public async Task<IReadOnlyList<SubcategorySummary>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);

        return await Context.Subcategorias
            .AsNoTracking()
            .OrderBy(subcategory => subcategory.CategoriaId)
            .ThenBy(subcategory => subcategory.Nombre)
            .Select(ToSummaryProjection)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SubcategorySummary>> GetActiveAsync(
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);

        return await Context.Subcategorias
            .AsNoTracking()
            .Where(subcategory =>
                subcategory.Activa &&
                subcategory.Categoria.Activa &&
                subcategory.Categoria.Nombre.Trim().ToUpper() != ProtectedCategoryNameNormalized)
            .OrderBy(subcategory => subcategory.CategoriaId)
            .ThenBy(subcategory => subcategory.Nombre)
            .Select(ToSummaryProjection)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SubcategorySummary>> GetByCategoryAsync(
        int categoriaId,
        bool soloActivas = false,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        var categoria = await EnsureCategoriaExistsAsync(categoriaId, cancellationToken);

        var query = Context.Subcategorias
            .AsNoTracking()
            .Where(subcategory => subcategory.CategoriaId == categoriaId);
        if (soloActivas)
        {
            if (!categoria.Activa || EsCategoriaSinCategoria(categoria))
            {
                return [];
            }

            query = query.Where(subcategory =>
                subcategory.Activa &&
                subcategory.Categoria.Activa &&
                subcategory.Categoria.Nombre.Trim().ToUpper() != ProtectedCategoryNameNormalized);
        }

        return await query
            .OrderBy(subcategory => subcategory.Nombre)
            .Select(ToSummaryProjection)
            .ToListAsync(cancellationToken);
    }

    public async Task<SubcategorySummary?> GetByIdAsync(
        int subcategoriaId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);

        return await Context.Subcategorias
            .AsNoTracking()
            .Where(subcategory => subcategory.SubcategoriaId == subcategoriaId)
            .Select(ToSummaryProjection)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<SubcategorySummary> CreateAsync(
        CreateSubcategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        var nombre = ValidarNombre(request.Nombre);
        var categoria = await EnsureCategoriaExistsAsync(request.CategoriaId, cancellationToken);

        if (EsCategoriaSinCategoria(categoria))
        {
            throw new InvalidOperationException(
                "La categoria 'Sin categoria' no puede tener subcategorias.");
        }

        if (!categoria.Activa)
        {
            throw new InvalidOperationException(
                "La categoria esta inactiva y no puede tener subcategorias asignables.");
        }

        await EnsureNombreUnicoAsync(categoria.CategoriaId, nombre, null, cancellationToken);

        var subcategoria = new Subcategoria
        {
            CategoriaId = categoria.CategoriaId,
            Nombre = nombre,
            Descripcion = NormalizarTextoOpcional(request.Descripcion),
            Activa = true,
            FechaCreacion = DateTime.UtcNow
        };

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        Context.Subcategorias.Add(subcategoria);
        await Context.SaveChangesAsync(cancellationToken);

        await RegistrarAuditoriaAsync(
            subcategoria.SubcategoriaId,
            $"Subcategoria '{subcategoria.Nombre}' creada para la categoria '{categoria.Nombre}'.",
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return ToSummary(subcategoria);
    }

    public async Task<SubcategorySummary> UpdateAsync(
        UpdateSubcategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        var nombre = ValidarNombre(request.Nombre);
        var subcategoria = await Context.Subcategorias
            .Include(item => item.Categoria)
            .FirstOrDefaultAsync(item => item.SubcategoriaId == request.SubcategoriaId, cancellationToken);

        if (subcategoria is null)
        {
            throw new InvalidOperationException("La subcategoria no existe.");
        }

        if (EsCategoriaSinCategoria(subcategoria.Categoria))
        {
            throw new InvalidOperationException(
                "La categoria 'Sin categoria' no puede tener subcategorias.");
        }

        await EnsureNombreUnicoAsync(
            subcategoria.CategoriaId,
            nombre,
            subcategoria.SubcategoriaId,
            cancellationToken);

        subcategoria.Nombre = nombre;
        subcategoria.Descripcion = NormalizarTextoOpcional(request.Descripcion);
        subcategoria.FechaModificacion = DateTime.UtcNow;

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        await Context.SaveChangesAsync(cancellationToken);

        await RegistrarAuditoriaAsync(
            subcategoria.SubcategoriaId,
            $"Subcategoria '{subcategoria.Nombre}' editada.",
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return ToSummary(subcategoria);
    }

    public async Task SetActiveAsync(
        int subcategoriaId,
        bool activa,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        var subcategoria = await Context.Subcategorias
            .Include(item => item.Categoria)
            .FirstOrDefaultAsync(item => item.SubcategoriaId == subcategoriaId, cancellationToken);

        if (subcategoria is null)
        {
            throw new InvalidOperationException("La subcategoria no existe.");
        }

        if (EsCategoriaSinCategoria(subcategoria.Categoria))
        {
            throw new InvalidOperationException(
                "La categoria 'Sin categoria' no puede tener subcategorias.");
        }

        if (activa && !subcategoria.Categoria.Activa)
        {
            throw new InvalidOperationException(
                "La categoria esta inactiva y la subcategoria no puede reactivarse.");
        }

        if (subcategoria.Activa == activa)
        {
            return;
        }

        subcategoria.Activa = activa;
        subcategoria.FechaModificacion = DateTime.UtcNow;

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        await Context.SaveChangesAsync(cancellationToken);

        await RegistrarAuditoriaAsync(
            subcategoria.SubcategoriaId,
            activa
                ? $"Subcategoria '{subcategoria.Nombre}' activada."
                : $"Subcategoria '{subcategoria.Nombre}' desactivada.",
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        int subcategoriaId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        var subcategoria = await Context.Subcategorias
            .Include(item => item.Categoria)
            .FirstOrDefaultAsync(item => item.SubcategoriaId == subcategoriaId, cancellationToken);

        if (subcategoria is null)
        {
            throw new InvalidOperationException("La subcategoria no existe.");
        }

        if (EsCategoriaSinCategoria(subcategoria.Categoria))
        {
            throw new InvalidOperationException(
                "No se puede eliminar una subcategoria protegida bajo 'Sin categoria'.");
        }

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        var hasProducts = await Context.Productos.AnyAsync(
            product => product.SubcategoriaId == subcategoriaId,
            cancellationToken);
        var hasSalesHistory = await Context.DetalleVentas.AnyAsync(
                detail => detail.SubcategoriaIdSnapshot == subcategoriaId,
                cancellationToken) ||
            await Context.DetallesCambioVenta.AnyAsync(
                detail => detail.SubcategoriaIdSnapshot == subcategoriaId,
                cancellationToken);

        if (hasProducts || hasSalesHistory)
        {
            throw new InvalidOperationException(
                "No se puede eliminar porque la subcategoria esta asignada a productos o aparece en historial comercial. " +
                "Desactivala para conservar sus referencias e historicos.");
        }

        Context.Subcategorias.Remove(subcategoria);
        await Context.SaveChangesAsync(cancellationToken);
        await RegistrarAuditoriaAsync(
            subcategoria.SubcategoriaId,
            $"Subcategoria '{subcategoria.Nombre}' eliminada de la categoria '{subcategoria.Categoria.Nombre}'.",
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<Categoria> EnsureCategoriaExistsAsync(
        int categoriaId,
        CancellationToken cancellationToken)
    {
        return await Context.Categorias
            .FirstOrDefaultAsync(category => category.CategoriaId == categoriaId, cancellationToken)
            ?? throw new InvalidOperationException("La categoria no existe.");
    }

    private async Task EnsureNombreUnicoAsync(
        int categoriaId,
        string nombre,
        int? subcategoriaIdActual,
        CancellationToken cancellationToken)
    {
        var nombreNormalizado = nombre.ToUpper();
        var existe = await Context.Subcategorias.AnyAsync(
            subcategory => subcategory.CategoriaId == categoriaId &&
                subcategory.Nombre.ToUpper() == nombreNormalizado &&
                (!subcategoriaIdActual.HasValue ||
                    subcategory.SubcategoriaId != subcategoriaIdActual.Value),
            cancellationToken);

        if (existe)
        {
            throw new InvalidOperationException(
                "Ya existe una subcategoria con ese nombre dentro de la categoria.");
        }
    }

    private async Task RegistrarAuditoriaAsync(
        int subcategoriaId,
        string descripcion,
        CancellationToken cancellationToken)
    {
        await _auditService.RegistrarAsync(
            new AuditEntryRequest(
                GetUsuarioActualId(),
                TipoBitacora.CONFIGURACION_MODIFICADA,
                "subcategorias",
                subcategoriaId,
                descripcion),
            cancellationToken);
    }

    private int GetUsuarioActualId()
    {
        return _currentSession.UsuarioActual?.UsuarioId
            ?? throw new InvalidOperationException(
                "Debe existir una sesion activa para administrar subcategorias.");
    }

    private static string ValidarNombre(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            throw new ArgumentException(
                "El nombre de la subcategoria es obligatorio.", nameof(nombre));
        }

        return nombre.Trim();
    }

    private static string? NormalizarTextoOpcional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool EsCategoriaSinCategoria(Categoria categoria)
    {
        return string.Equals(
            categoria.Nombre.Trim(),
            CategoryService.DefaultCategoryName,
            StringComparison.OrdinalIgnoreCase);
    }

    private static SubcategorySummary ToSummary(Subcategoria subcategoria)
    {
        return new SubcategorySummary(
            subcategoria.SubcategoriaId,
            subcategoria.CategoriaId,
            subcategoria.Nombre,
            subcategoria.Descripcion,
            subcategoria.Activa);
    }

    private static readonly Expression<Func<Subcategoria, SubcategorySummary>> ToSummaryProjection =
        subcategory => new SubcategorySummary(
            subcategory.SubcategoriaId,
            subcategory.CategoriaId,
            subcategory.Nombre,
            subcategory.Descripcion,
            subcategory.Activa);
}
