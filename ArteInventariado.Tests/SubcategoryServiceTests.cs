using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class SubcategoryServiceTests
{
    [Fact]
    public async Task CreateAsync_TrimsNameAndDescriptionAndReturnsSummary()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Subcategories.CreateAsync(new CreateSubcategoryRequest(
            fixture.CategoryA.CategoriaId,
            "  Ollas  ",
            "  Piezas para cocina  "));

        Assert.Equal(fixture.CategoryA.CategoriaId, result.CategoriaId);
        Assert.Equal("Ollas", result.Nombre);
        Assert.Equal("Piezas para cocina", result.Descripcion);
        Assert.True(result.Activa);
        Assert.Single(await fixture.Context.Subcategorias.ToListAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_RejectsMissingName(string nombre)
    {
        await using var fixture = await Fixture.CreateAsync();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Subcategories.CreateAsync(new CreateSubcategoryRequest(
                fixture.CategoryA.CategoriaId,
                nombre)));

        Assert.StartsWith("El nombre de la subcategoria es obligatorio.", exception.Message);
        Assert.Equal("nombre", exception.ParamName);
        Assert.Empty(await fixture.Context.Subcategorias.ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_RejectsNonexistentCategory()
    {
        await using var fixture = await Fixture.CreateAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Subcategories.CreateAsync(new CreateSubcategoryRequest(9999, "Ollas")));

        Assert.Equal("La categoria no existe.", exception.Message);
        Assert.Empty(await fixture.Context.Subcategorias.ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_RejectsInactiveParentCategory()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetCategoryActiveAsync(fixture.CategoryA, false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Subcategories.CreateAsync(new CreateSubcategoryRequest(
                fixture.CategoryA.CategoriaId,
                "Ollas")));

        Assert.Contains("inactiva", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await fixture.Context.Subcategorias.ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateWithinCategoryIgnoringCase()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Subcategories.CreateAsync(new CreateSubcategoryRequest(
            fixture.CategoryA.CategoriaId,
            "Ollas"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Subcategories.CreateAsync(new CreateSubcategoryRequest(
                fixture.CategoryA.CategoriaId,
                "  ollas  ")));

        Assert.Contains("dentro de la categoria", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await fixture.Context.Subcategorias.ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_AllowsSameNameInDifferentCategories()
    {
        await using var fixture = await Fixture.CreateAsync();

        var first = await fixture.Subcategories.CreateAsync(new CreateSubcategoryRequest(
            fixture.CategoryA.CategoriaId,
            "Ollas"));
        var second = await fixture.Subcategories.CreateAsync(new CreateSubcategoryRequest(
            fixture.CategoryB.CategoriaId,
            "Ollas"));

        Assert.NotEqual(first.SubcategoriaId, second.SubcategoriaId);
        Assert.Equal(2, await fixture.Context.Subcategorias.CountAsync());
    }

    [Fact]
    public async Task GetByCategoryAsync_ReturnsAllOrOnlyActiveSubcategories()
    {
        await using var fixture = await Fixture.CreateAsync();
        var inactive = await fixture.Subcategories.CreateAsync(new CreateSubcategoryRequest(
            fixture.CategoryA.CategoriaId,
            "Inactiva"));
        await fixture.Subcategories.CreateAsync(new CreateSubcategoryRequest(
            fixture.CategoryA.CategoriaId,
            "Activa"));
        await fixture.Subcategories.SetActiveAsync(inactive.SubcategoriaId, false);

        var all = await fixture.Subcategories.GetByCategoryAsync(fixture.CategoryA.CategoriaId);
        var active = await fixture.Subcategories.GetByCategoryAsync(
            fixture.CategoryA.CategoriaId,
            soloActivas: true);

        Assert.Equal(["Activa", "Inactiva"], all.Select(item => item.Nombre));
        Assert.Equal(["Activa"], active.Select(item => item.Nombre));

        await fixture.Subcategories.SetActiveAsync(inactive.SubcategoriaId, true);
        var reactivated = await fixture.Subcategories.GetByCategoryAsync(
            fixture.CategoryA.CategoriaId,
            soloActivas: true);
        Assert.Equal(["Activa", "Inactiva"], reactivated.Select(item => item.Nombre));
    }

    [Fact]
    public async Task GetActiveAsync_ExcludesSubcategoriesWhoseParentIsInactive()
    {
        await using var fixture = await Fixture.CreateAsync();
        var inactiveParentSubcategory = await fixture.Subcategories.CreateAsync(
            new CreateSubcategoryRequest(fixture.CategoryA.CategoriaId, "Ollas"));
        var activeParentSubcategory = await fixture.Subcategories.CreateAsync(
            new CreateSubcategoryRequest(fixture.CategoryB.CategoriaId, "Tazas"));
        await fixture.SetCategoryActiveAsync(fixture.CategoryA, false);

        var active = await fixture.Subcategories.GetActiveAsync();

        Assert.DoesNotContain(active, item => item.SubcategoriaId == inactiveParentSubcategory.SubcategoriaId);
        Assert.Contains(active, item => item.SubcategoriaId == activeParentSubcategory.SubcategoriaId);
    }

    [Fact]
    public async Task GetActiveAsync_ExcludesSubcategoriesUnderProtectedDefaultCategory()
    {
        await using var fixture = await Fixture.CreateAsync();
        var defaultCategory = new Categoria
        {
            Nombre = CategoryService.DefaultCategoryName,
            Activa = true,
            FechaCreacion = DateTime.UtcNow
        };
        fixture.Context.Categorias.Add(defaultCategory);
        await fixture.Context.SaveChangesAsync();

        var protectedSubcategory = new Subcategoria
        {
            CategoriaId = defaultCategory.CategoriaId,
            Nombre = "No asignable",
            Activa = true,
            FechaCreacion = DateTime.UtcNow
        };
        fixture.Context.Subcategorias.Add(protectedSubcategory);
        await fixture.Context.SaveChangesAsync();

        var active = await fixture.Subcategories.GetActiveAsync();
        var assignable = await fixture.Subcategories.GetByCategoryAsync(
            defaultCategory.CategoriaId,
            soloActivas: true);

        Assert.DoesNotContain(active, item => item.SubcategoriaId == protectedSubcategory.SubcategoriaId);
        Assert.Empty(assignable);
    }

    [Fact]
    public async Task GetByCategoryAsync_ActiveOnlyReturnsEmptyForInactiveParentButAllRemainsVisible()
    {
        await using var fixture = await Fixture.CreateAsync();
        var activeChild = await fixture.Subcategories.CreateAsync(
            new CreateSubcategoryRequest(fixture.CategoryA.CategoriaId, "Activa"));
        var inactiveChild = await fixture.Subcategories.CreateAsync(
            new CreateSubcategoryRequest(fixture.CategoryA.CategoriaId, "Inactiva"));
        await fixture.Subcategories.SetActiveAsync(inactiveChild.SubcategoriaId, false);
        await fixture.SetCategoryActiveAsync(fixture.CategoryA, false);

        var all = await fixture.Subcategories.GetByCategoryAsync(
            fixture.CategoryA.CategoriaId,
            soloActivas: false);
        var assignable = await fixture.Subcategories.GetByCategoryAsync(
            fixture.CategoryA.CategoriaId,
            soloActivas: true);

        Assert.Equal(
            [activeChild.Nombre, inactiveChild.Nombre],
            all.Select(item => item.Nombre));
        Assert.Empty(assignable);
    }

    [Fact]
    public async Task SetActiveAsync_RejectsReactivationWhenParentIsInactiveAndAllowsItAfterParentReactivates()
    {
        await using var fixture = await Fixture.CreateAsync();
        var subcategory = await fixture.Subcategories.CreateAsync(
            new CreateSubcategoryRequest(fixture.CategoryA.CategoriaId, "Ollas"));
        await fixture.Subcategories.SetActiveAsync(subcategory.SubcategoriaId, false);
        await fixture.SetCategoryActiveAsync(fixture.CategoryA, false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Subcategories.SetActiveAsync(subcategory.SubcategoriaId, true));

        Assert.Contains("inactiva", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(await fixture.Context.Subcategorias
            .Where(item => item.SubcategoriaId == subcategory.SubcategoriaId)
            .Select(item => item.Activa)
            .SingleAsync());

        await fixture.SetCategoryActiveAsync(fixture.CategoryA, true);
        await fixture.Subcategories.SetActiveAsync(subcategory.SubcategoriaId, true);

        var active = await fixture.Subcategories.GetActiveAsync();
        Assert.Contains(active, item => item.SubcategoriaId == subcategory.SubcategoriaId);
    }

    [Fact]
    public async Task UpdateAsync_TrimsValuesAndKeepsParentCategory()
    {
        await using var fixture = await Fixture.CreateAsync();
        var subcategory = await fixture.Subcategories.CreateAsync(new CreateSubcategoryRequest(
            fixture.CategoryA.CategoriaId,
            "Ollas"));

        var updated = await fixture.Subcategories.UpdateAsync(new UpdateSubcategoryRequest(
            subcategory.SubcategoriaId,
            "  Cazuelas  ",
            "  Ceramica de cocina  "));

        Assert.Equal(fixture.CategoryA.CategoriaId, updated.CategoriaId);
        Assert.Equal("Cazuelas", updated.Nombre);
        Assert.Equal("Ceramica de cocina", updated.Descripcion);
    }

    [Fact]
    public async Task GetByIdAsync_MissingSubcategoryReturnsNull()
    {
        await using var fixture = await Fixture.CreateAsync();

        Assert.Null(await fixture.Subcategories.GetByIdAsync(9999));
    }

    [Fact]
    public async Task DeleteAsync_RemovesUnassignedSubcategoryAndAuditsIt()
    {
        await using var fixture = await Fixture.CreateAsync();
        var subcategory = await fixture.Subcategories.CreateAsync(
            new CreateSubcategoryRequest(fixture.CategoryA.CategoriaId, "Temporal"));

        await fixture.Subcategories.DeleteAsync(subcategory.SubcategoriaId);

        Assert.False(await fixture.Context.Subcategorias.AnyAsync(
            item => item.SubcategoriaId == subcategory.SubcategoriaId));
        Assert.Contains(await fixture.Context.Bitacora.ToListAsync(), entry =>
            entry.EntidadId == subcategory.SubcategoriaId &&
            entry.Descripcion.Contains("eliminada", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DeleteAsync_RejectsSubcategoryAssignedToProduct()
    {
        await using var fixture = await Fixture.CreateAsync();
        var subcategory = await fixture.Subcategories.CreateAsync(
            new CreateSubcategoryRequest(fixture.CategoryA.CategoriaId, "Asignada"));
        await fixture.Products.CreateAsync(fixture.CreateProductRequest(
            "Producto asignado",
            fixture.CategoryA.CategoriaId,
            subcategory.SubcategoriaId));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Subcategories.DeleteAsync(subcategory.SubcategoriaId));

        Assert.Contains("asignada a productos", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(await fixture.Context.Subcategorias.AnyAsync(
            item => item.SubcategoriaId == subcategory.SubcategoriaId));
    }

    [Fact]
    public async Task DeleteAsync_RejectsSubcategoryReferencedByHistoricalSaleSnapshot()
    {
        await using var fixture = await Fixture.CreateAsync();
        var historicalSubcategory = await fixture.Subcategories.CreateAsync(new CreateSubcategoryRequest(
            fixture.CategoryA.CategoriaId,
            "Subcategoria historica"));
        var product = await fixture.Products.CreateAsync(fixture.CreateProductRequest(
            "Producto movido de categoria",
            fixture.CategoryB.CategoriaId,
            null));
        var userId = await fixture.Context.Usuarios.Select(user => user.UsuarioId).SingleAsync();
        var cash = new Caja
        {
            FechaOperacion = DateOnly.FromDateTime(DateTime.UtcNow),
            UsuarioAperturaId = userId,
            Estado = EstadoCaja.CERRADA,
            FechaCierre = DateTime.UtcNow
        };
        fixture.Context.Cajas.Add(cash);
        await fixture.Context.SaveChangesAsync();
        var sale = new Venta
        {
            NumeroVenta = "VTA-HIST-SUBCATEGORY",
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
            CategoriaIdSnapshot = fixture.CategoryA.CategoriaId,
            CategoriaNombreSnapshot = fixture.CategoryA.Nombre,
            SubcategoriaIdSnapshot = historicalSubcategory.SubcategoriaId,
            SubcategoriaNombreSnapshot = historicalSubcategory.Nombre,
            SubtotalCentavos = 200,
            DescuentoCentavos = 0,
            DescuentoGeneralProporcionalCentavos = 0,
            TotalCentavos = 200
        });
        await fixture.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Subcategories.DeleteAsync(historicalSubcategory.SubcategoriaId));

        Assert.Contains("historial comercial", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(await fixture.Context.Subcategorias.AnyAsync(item =>
            item.SubcategoriaId == historicalSubcategory.SubcategoriaId));
    }

    [Fact]
    public async Task CreateAsync_RejectsProtectedDefaultCategory()
    {
        await using var fixture = await Fixture.CreateAsync();
        var defaultCategory = new Categoria
        {
            Nombre = CategoryService.DefaultCategoryName,
            Activa = true,
            FechaCreacion = DateTime.UtcNow
        };
        fixture.Context.Categorias.Add(defaultCategory);
        await fixture.Context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Subcategories.CreateAsync(new CreateSubcategoryRequest(
                defaultCategory.CategoriaId,
                "No permitida")));

        Assert.Contains("Sin categoria", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await fixture.Context.Subcategorias.ToListAsync());
    }

    [Fact]
    public async Task ProductService_AllowsCompatibleSubcategoryAndProjectsId()
    {
        await using var fixture = await Fixture.CreateAsync();
        var subcategory = await fixture.Subcategories.CreateAsync(new CreateSubcategoryRequest(
            fixture.CategoryA.CategoriaId,
            "Ollas"));

        var product = await fixture.Products.CreateAsync(fixture.CreateProductRequest(
            "Olla artesanal",
            fixture.CategoryA.CategoriaId,
            subcategory.SubcategoriaId));

        Assert.Equal(subcategory.SubcategoriaId, product.SubcategoriaId);
        Assert.Equal(
            subcategory.SubcategoriaId,
            await fixture.Context.Productos
                .Where(item => item.ProductoId == product.ProductoId)
                .Select(item => item.SubcategoriaId)
                .SingleAsync());
    }

    [Fact]
    public async Task ProductService_ProjectsSubcategoryNameAndSearchesByIt()
    {
        await using var fixture = await Fixture.CreateAsync();
        var subcategory = await fixture.Subcategories.CreateAsync(new CreateSubcategoryRequest(
            fixture.CategoryA.CategoriaId,
            "Ollas"));
        var product = await fixture.Products.CreateAsync(fixture.CreateProductRequest(
            "Producto de barro",
            fixture.CategoryA.CategoriaId,
            subcategory.SubcategoriaId));

        var projected = await fixture.Products.GetByCodigoInternoAsync(product.CodigoInterno);
        Assert.NotNull(projected);
        Assert.Equal(subcategory.SubcategoriaId, projected.SubcategoriaId);
        Assert.Equal("Ollas", projected.SubcategoriaNombre);
        Assert.Single(await fixture.Products.GetAllAsync(new ProductFilter(SearchText: "Ollas")));
        Assert.Single(await fixture.Products.SearchByNameAsync("Ollas"));
        Assert.Single(await fixture.Products.GetAllAsync(new ProductFilter(SubcategoriaId: subcategory.SubcategoriaId)));
    }

    [Fact]
    public async Task ProductService_AllowsKeepingExistingInactiveSubcategoryDuringOtherEdit()
    {
        await using var fixture = await Fixture.CreateAsync();
        var subcategory = await fixture.Subcategories.CreateAsync(new CreateSubcategoryRequest(
            fixture.CategoryA.CategoriaId,
            "Ollas"));
        var product = await fixture.Products.CreateAsync(fixture.CreateProductRequest(
            "Producto historico",
            fixture.CategoryA.CategoriaId,
            subcategory.SubcategoriaId));
        await fixture.Subcategories.SetActiveAsync(subcategory.SubcategoriaId, false);

        var updated = await fixture.Products.UpdateAsync(new UpdateProductRequest(
            product.ProductoId,
            product.CodigoInterno,
            "Producto historico editado",
            product.Descripcion,
            product.CategoriaId,
            product.ImagenLocalPath,
            product.PrecioCompraCentavos,
            product.PrecioVentaCentavos,
            product.StockMinimo,
            product.Modo,
            subcategory.SubcategoriaId));

        Assert.Equal("Producto historico editado", updated.Nombre);
        Assert.Equal(subcategory.SubcategoriaId, updated.SubcategoriaId);
        Assert.Equal("Ollas", updated.SubcategoriaNombre);
    }

    [Fact]
    public async Task ProductService_RejectsNewInactiveSubcategoryAssignmentDuringUpdate()
    {
        await using var fixture = await Fixture.CreateAsync();
        var subcategory = await fixture.Subcategories.CreateAsync(new CreateSubcategoryRequest(
            fixture.CategoryA.CategoriaId,
            "Ollas"));
        var product = await fixture.Products.CreateAsync(fixture.CreateProductRequest(
            "Producto sin clasificar",
            fixture.CategoryA.CategoriaId,
            null));
        await fixture.Subcategories.SetActiveAsync(subcategory.SubcategoriaId, false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Products.UpdateAsync(new UpdateProductRequest(
                product.ProductoId,
                product.CodigoInterno,
                product.Nombre,
                product.Descripcion,
                product.CategoriaId,
                product.ImagenLocalPath,
                product.PrecioCompraCentavos,
                product.PrecioVentaCentavos,
                product.StockMinimo,
                product.Modo,
                subcategory.SubcategoriaId)));

        Assert.Contains("inactiva", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(await fixture.Context.Productos
            .Where(item => item.ProductoId == product.ProductoId)
            .Select(item => item.SubcategoriaId)
            .SingleAsync());
    }

    [Fact]
    public async Task ProductService_RejectsSubcategoryFromAnotherCategory()
    {
        await using var fixture = await Fixture.CreateAsync();
        var subcategory = await fixture.Subcategories.CreateAsync(new CreateSubcategoryRequest(
            fixture.CategoryA.CategoriaId,
            "Ollas"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Products.CreateAsync(fixture.CreateProductRequest(
                "Producto invalido",
                fixture.CategoryB.CategoriaId,
                subcategory.SubcategoriaId)));

        Assert.Contains("no pertenece", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await fixture.Context.Productos.ToListAsync());
    }

    [Fact]
    public async Task ProductService_RejectsInactiveSubcategoryForNewAssignment()
    {
        await using var fixture = await Fixture.CreateAsync();
        var subcategory = await fixture.Subcategories.CreateAsync(new CreateSubcategoryRequest(
            fixture.CategoryA.CategoriaId,
            "Ollas"));
        await fixture.Subcategories.SetActiveAsync(subcategory.SubcategoriaId, false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Products.CreateAsync(fixture.CreateProductRequest(
                "Producto inactivo",
                fixture.CategoryA.CategoriaId,
                subcategory.SubcategoriaId)));

        Assert.Contains("inactiva", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await fixture.Context.Productos.ToListAsync());
    }

    [Fact]
    public async Task ProductService_RejectsSubcategoryWhenParentCategoryIsInactive()
    {
        await using var fixture = await Fixture.CreateAsync();
        var subcategory = await fixture.Subcategories.CreateAsync(
            new CreateSubcategoryRequest(fixture.CategoryA.CategoriaId, "Ollas"));
        await fixture.SetCategoryActiveAsync(fixture.CategoryA, false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Products.CreateAsync(fixture.CreateProductRequest(
                "Producto con categoria inactiva",
                fixture.CategoryA.CategoriaId,
                subcategory.SubcategoriaId)));

        Assert.Contains("categoria", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inactiva", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await fixture.Context.Productos.ToListAsync());
    }

    [Fact]
    public async Task ProductService_RejectsSubcategoryForProtectedDefaultCategory()
    {
        await using var fixture = await Fixture.CreateAsync();
        var defaultCategory = new Categoria
        {
            Nombre = CategoryService.DefaultCategoryName,
            Activa = true,
            FechaCreacion = DateTime.UtcNow
        };
        fixture.Context.Categorias.Add(defaultCategory);
        await fixture.Context.SaveChangesAsync();
        var subcategory = new Subcategoria
        {
            CategoriaId = defaultCategory.CategoriaId,
            Nombre = "No permitida",
            Activa = true,
            FechaCreacion = DateTime.UtcNow
        };
        fixture.Context.Subcategorias.Add(subcategory);
        await fixture.Context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Products.CreateAsync(fixture.CreateProductRequest(
                "Producto invalido",
                defaultCategory.CategoriaId,
                subcategory.SubcategoriaId)));

        Assert.Contains("Sin categoria", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await fixture.Context.Productos.ToListAsync());
    }

    [Fact]
    public async Task ProductService_RejectsImplicitDefaultWithSubcategoryBeforeCreatingDefaultCategory()
    {
        await using var fixture = await Fixture.CreateAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Products.CreateAsync(fixture.CreateProductRequest(
                "Producto invalido",
                categoryId: 0,
                subcategoryId: 12345) with { CategoriaId = null }));

        Assert.Contains("Sin categoria", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await fixture.Context.Productos.ToListAsync());
        Assert.DoesNotContain(
            await fixture.Context.Categorias.ToListAsync(),
            category => string.Equals(
                category.Nombre,
                CategoryService.DefaultCategoryName,
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProductService_AllowsProductWithoutSubcategory()
    {
        await using var fixture = await Fixture.CreateAsync();

        var product = await fixture.Products.CreateAsync(fixture.CreateProductRequest(
            "Producto sin subcategoria",
            fixture.CategoryA.CategoriaId,
            null));

        Assert.Null(product.SubcategoriaId);
        var projected = await fixture.Products.GetByCodigoInternoAsync(product.CodigoInterno);
        Assert.NotNull(projected);
        Assert.Null(projected.SubcategoriaId);
    }

    [Fact]
    public async Task ProductService_RejectsIncompatibleCategoryChangeWithoutReassignment()
    {
        await using var fixture = await Fixture.CreateAsync();
        var subcategory = await fixture.Subcategories.CreateAsync(new CreateSubcategoryRequest(
            fixture.CategoryA.CategoriaId,
            "Ollas"));
        var product = await fixture.Products.CreateAsync(fixture.CreateProductRequest(
            "Olla artesanal",
            fixture.CategoryA.CategoriaId,
            subcategory.SubcategoriaId));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Products.UpdateAsync(new UpdateProductRequest(
                product.ProductoId,
                product.CodigoInterno,
                product.Nombre,
                product.Descripcion,
                fixture.CategoryB.CategoriaId,
                product.ImagenLocalPath,
                product.PrecioCompraCentavos,
                product.PrecioVentaCentavos,
                product.StockMinimo,
                product.Modo,
                subcategory.SubcategoriaId)));

        Assert.Contains("no pertenece", exception.Message, StringComparison.OrdinalIgnoreCase);
        var stored = await fixture.Context.Productos.AsNoTracking()
            .SingleAsync(item => item.ProductoId == product.ProductoId);
        Assert.Equal(fixture.CategoryA.CategoriaId, stored.CategoriaId);
        Assert.Equal(subcategory.SubcategoriaId, stored.SubcategoriaId);
    }

    [Fact]
    public async Task DatabaseUniqueIndexRejectsCaseInsensitiveDuplicateWithinCategory()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Context.Subcategorias.AddRange(
            new Subcategoria
            {
                CategoriaId = fixture.CategoryA.CategoriaId,
                Nombre = "Ollas",
                FechaCreacion = DateTime.UtcNow
            },
            new Subcategoria
            {
                CategoriaId = fixture.CategoryA.CategoriaId,
                Nombre = "ollas",
                FechaCreacion = DateTime.UtcNow
            });

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Context.SaveChangesAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            SqliteConnection connection,
            AppDbContext context,
            SubcategoryService subcategories,
            ProductService products,
            Categoria categoryA,
            Categoria categoryB)
        {
            Connection = connection;
            Context = context;
            Subcategories = subcategories;
            Products = products;
            CategoryA = categoryA;
            CategoryB = categoryB;
        }

        private SqliteConnection Connection { get; }
        public AppDbContext Context { get; }
        public SubcategoryService Subcategories { get; }
        public ProductService Products { get; }
        public Categoria CategoryA { get; }
        public Categoria CategoryB { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options);
            await context.Database.EnsureCreatedAsync();

            var user = new Usuario
            {
                NombreCompleto = "Administrador de pruebas",
                NombreUsuario = $"admin-subcategory-{Guid.NewGuid():N}",
                PasswordHash = "hash",
                Rol = RolUsuario.ADMIN,
                Activo = true,
                FechaCreacion = DateTime.UtcNow
            };
            var categoryA = new Categoria
            {
                Nombre = $"Barro negro-{Guid.NewGuid():N}",
                Activa = true,
                FechaCreacion = DateTime.UtcNow
            };
            var categoryB = new Categoria
            {
                Nombre = $"Ceramica-{Guid.NewGuid():N}",
                Activa = true,
                FechaCreacion = DateTime.UtcNow
            };
            context.AddRange(user, categoryA, categoryB);
            await context.SaveChangesAsync();

            var session = new TestSession(new UsuarioSesion(
                user.UsuarioId,
                user.NombreCompleto,
                user.NombreUsuario,
                user.Rol));
            var audit = new AuditService(context);
            var subcategories = new SubcategoryService(context, audit, session);
            var products = new ProductService(context, audit, session);
            return new Fixture(connection, context, subcategories, products, categoryA, categoryB);
        }

        public CreateProductRequest CreateProductRequest(
            string name,
            int categoryId,
            int? subcategoryId)
        {
            return new CreateProductRequest(
                CodigoInterno: null,
                Nombre: name,
                Descripcion: null,
                CategoriaId: categoryId,
                ImagenLocalPath: null,
                PrecioCompraCentavos: 100,
                PrecioVentaCentavos: 200,
                StockActual: 0,
                StockMinimo: 0,
                Modo: ModoProducto.SIMPLE,
                SubcategoriaId: subcategoryId);
        }

        public async Task SetCategoryActiveAsync(Categoria category, bool active)
        {
            category.Activa = active;
            await Context.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    private sealed class TestSession(UsuarioSesion user) : ICurrentSession
    {
        public UsuarioSesion? UsuarioActual { get; private set; } = user;
        public bool EstaAutenticado => UsuarioActual is not null;
        public bool EsAdmin => UsuarioActual?.Rol == RolUsuario.ADMIN;
        public bool TieneRol(RolUsuario rol) => UsuarioActual?.Rol == rol;
        public void IniciarSesion(UsuarioSesion usuario) => UsuarioActual = usuario;
        public void CerrarSesion() => UsuarioActual = null;
    }
}
