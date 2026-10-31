using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class CategoryServiceTests
{
    [Fact]
    public async Task GetActiveAsync_ReturnsOnlyActiveCategoriesInNameOrder()
    {
        await using var fixture = await CategoryFixture.CreateAsync();
        fixture.Context.Categorias.AddRange(
            new Categoria { Nombre = "Zeta", Activa = true },
            new Categoria { Nombre = "Alfa", Activa = true },
            new Categoria { Nombre = "Inactiva", Activa = false });
        await fixture.Context.SaveChangesAsync();

        var categories = await fixture.Service.GetActiveAsync();

        Assert.NotNull(categories);
        Assert.Equal(["Alfa", "Zeta"], categories.Select(category => category.Nombre));
        Assert.All(categories, category => Assert.True(category.Activa));
    }

    [Fact]
    public async Task GetActiveAsync_WithNoActiveCategories_ReturnsEmptyCollection()
    {
        await using var fixture = await CategoryFixture.CreateAsync();
        fixture.Context.Categorias.Add(new Categoria { Nombre = "Inactiva", Activa = false });
        await fixture.Context.SaveChangesAsync();

        var categories = await fixture.Service.GetActiveAsync();

        Assert.NotNull(categories);
        Assert.Empty(categories);
    }

    [Fact]
    public void EnsureCategoryCollection_WithNullResult_ReturnsEmptyCollection()
    {
        var categories = CategoryService.EnsureCategoryCollection(null);
        var filterNames = categories
            .Select(category => category.Nombre)
            .Prepend("Todas")
            .ToList();

        Assert.NotNull(categories);
        Assert.Empty(categories);
        Assert.Equal(["Todas"], filterNames);
    }

    [Fact]
    public async Task DeleteAsync_RemovesUnusedCategoryAndAuditsIt()
    {
        await using var fixture = await CategoryFixture.CreateAsync();
        var category = new Categoria { Nombre = "Temporal", Activa = true, FechaCreacion = DateTime.UtcNow };
        fixture.Context.Categorias.Add(category);
        await fixture.Context.SaveChangesAsync();

        await fixture.Service.DeleteAsync(category.CategoriaId);

        Assert.False(await fixture.Context.Categorias.AnyAsync(item => item.CategoriaId == category.CategoriaId));
        Assert.Contains(await fixture.Context.Bitacora.ToListAsync(), entry =>
            entry.EntidadId == category.CategoriaId &&
            entry.Descripcion.Contains("eliminada", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DeleteAsync_RejectsCategoryWithSubcategories()
    {
        await using var fixture = await CategoryFixture.CreateAsync();
        var category = new Categoria { Nombre = "En uso", Activa = true, FechaCreacion = DateTime.UtcNow };
        fixture.Context.Categorias.Add(category);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.Subcategorias.Add(new Subcategoria
        {
            CategoriaId = category.CategoriaId,
            Nombre = "Hijas",
            FechaCreacion = DateTime.UtcNow
        });
        await fixture.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.DeleteAsync(category.CategoriaId));

        Assert.Contains("subcategorias", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(await fixture.Context.Categorias.AnyAsync(item => item.CategoriaId == category.CategoriaId));
    }

    [Fact]
    public async Task DeleteAsync_ProtectsDefaultCategory()
    {
        await using var fixture = await CategoryFixture.CreateAsync();
        var category = new Categoria
        {
            Nombre = CategoryService.DefaultCategoryName,
            Activa = true,
            FechaCreacion = DateTime.UtcNow
        };
        fixture.Context.Categorias.Add(category);
        await fixture.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.DeleteAsync(category.CategoriaId));

        Assert.Contains("protegida", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(await fixture.Context.Categorias.AnyAsync(item => item.CategoriaId == category.CategoriaId));
    }

    [Fact]
    public async Task DeleteAsync_RejectsCategoryReferencedByHistoricalSaleSnapshot()
    {
        await using var fixture = await CategoryFixture.CreateAsync();
        var historicalCategory = new Categoria
        {
            Nombre = "Categoria historica",
            Activa = true,
            FechaCreacion = DateTime.UtcNow
        };
        var currentCategory = new Categoria
        {
            Nombre = CategoryService.DefaultCategoryName,
            Activa = true,
            FechaCreacion = DateTime.UtcNow
        };
        fixture.Context.Categorias.AddRange(historicalCategory, currentCategory);
        await fixture.Context.SaveChangesAsync();

        var product = new Producto
        {
            CodigoInterno = "PRD-HIST-CATEGORY",
            Nombre = "Producto historico",
            CategoriaId = currentCategory.CategoriaId,
            PrecioCompraCentavos = 100,
            PrecioVentaCentavos = 200,
            FechaCreacion = DateTime.UtcNow
        };
        var userId = await fixture.Context.Usuarios.Select(user => user.UsuarioId).SingleAsync();
        var cash = new Caja
        {
            FechaOperacion = DateOnly.FromDateTime(DateTime.UtcNow),
            UsuarioAperturaId = userId,
            Estado = EstadoCaja.CERRADA,
            FechaCierre = DateTime.UtcNow
        };
        fixture.Context.AddRange(product, cash);
        await fixture.Context.SaveChangesAsync();

        var sale = new Venta
        {
            NumeroVenta = "VTA-HIST-CATEGORY",
            CajaId = cash.CajaId,
            UsuarioId = userId,
            SubtotalCentavos = 200,
            TotalCentavos = 200,
            PagadoCentavos = 200,
            Estado = EstadoVenta.COMPLETADA
        };
        fixture.Context.Ventas.Add(sale);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.DetalleVentas.Add(new DetalleVenta
        {
            VentaId = sale.VentaId,
            ProductoId = product.ProductoId,
            Cantidad = 1,
            PrecioUnitarioCentavos = 200,
            ProductoCodigoSnapshot = product.CodigoInterno,
            ProductoNombreSnapshot = product.Nombre,
            CategoriaIdSnapshot = historicalCategory.CategoriaId,
            CategoriaNombreSnapshot = historicalCategory.Nombre,
            SubtotalCentavos = 200,
            DescuentoCentavos = 0,
            TotalCentavos = 200
        });
        await fixture.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.DeleteAsync(historicalCategory.CategoriaId));

        Assert.Contains("historial comercial", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(await fixture.Context.Categorias.AnyAsync(item =>
            item.CategoriaId == historicalCategory.CategoriaId));
    }

    private sealed class CategoryFixture : IAsyncDisposable
    {
        private CategoryFixture(SqliteConnection connection, AppDbContext context, CategoryService service)
        {
            Connection = connection;
            Context = context;
            Service = service;
        }

        private SqliteConnection Connection { get; }
        public AppDbContext Context { get; }
        public CategoryService Service { get; }

        public static async Task<CategoryFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite(connection)
                    .Options);
            await context.Database.EnsureCreatedAsync();

            var user = new Usuario
            {
                NombreCompleto = "Administrador de pruebas",
                NombreUsuario = $"admin-category-{Guid.NewGuid():N}",
                PasswordHash = "hash",
                Rol = RolUsuario.ADMIN,
                Activo = true,
                FechaCreacion = DateTime.UtcNow
            };
            context.Usuarios.Add(user);
            await context.SaveChangesAsync();
            var session = new CurrentSession();
            session.IniciarSesion(new UsuarioSesion(
                user.UsuarioId, user.NombreCompleto, user.NombreUsuario, user.Rol));
            var audit = new AuditService(context);

            return new CategoryFixture(
                connection,
                context,
                new CategoryService(context, audit, session));
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
