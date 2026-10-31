using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public class SaleServiceTests
{
    [Fact]
    public async Task RegisterSaleAsync_PersistsCashPaymentWithoutDiscount()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        var result = await fixture.Service.RegisterSaleAsync(new RegisterSaleRequest(
            [new SaleLineRequest(fixture.ProductoId, 1)],
            [new SalePaymentRequest(MetodoPago.EFECTIVO, 2150)]));

        Assert.Equal(2150, result.SubtotalCentavos);
        Assert.Equal(0, result.DescuentoCentavos);
        Assert.Equal(2150, result.TotalCentavos);
        Assert.Equal(2150, result.TotalPagadoCentavos);
        Assert.Equal(0, result.CambioCentavos);

        var payment = Assert.Single(result.Payments);
        Assert.Equal(MetodoPago.EFECTIVO, payment.MetodoPago);
        Assert.Equal(2150, payment.MontoCentavos);

        await AssertPersistedCountsAsync(fixture.Context, ventas: 1, detalles: 1, pagos: 1);
        var storedSale = await fixture.Context.Ventas.AsNoTracking().SingleAsync(v => v.VentaId == result.VentaId);
        Assert.Equal(2150, storedSale.PagadoCentavos);
        Assert.Equal(0, storedSale.CambioCentavos);
    }

    [Fact]
    public async Task RegisterSaleAsync_PersistsHistoricalProductCostCategoryAndUserSnapshots()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        var result = await fixture.Service.RegisterSaleAsync(CashSale(fixture.ProductoId));
        var sale = await fixture.Context.Ventas.AsNoTracking().SingleAsync(v => v.VentaId == result.VentaId);
        var line = await fixture.Context.DetalleVentas.AsNoTracking().SingleAsync(d => d.VentaId == result.VentaId);

        Assert.Equal("Empleado de pruebas", sale.UsuarioNombreSnapshot);
        Assert.Equal(1000, line.CostoUnitarioCentavos);
        Assert.Equal(OrigenCostoHistorico.PRODUCTO_ACTUAL, line.OrigenCosto);
        Assert.StartsWith("REZE-", line.ProductoCodigoSnapshot);
        Assert.Equal("reze", line.ProductoNombreSnapshot);
        Assert.NotNull(line.CategoriaIdSnapshot);
        Assert.StartsWith("Categoria-", line.CategoriaNombreSnapshot);
        Assert.Null(line.SubcategoriaIdSnapshot);
        Assert.Equal("Sin subcategoría", line.SubcategoriaNombreSnapshot);
        Assert.Equal(0, line.DescuentoGeneralProporcionalCentavos);
    }

    [Fact]
    public async Task RegisterSaleAsync_CapturesSubcategorySnapshotAndPreservesItAfterCatalogChange()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        var product = await fixture.Context.Productos.SingleAsync(item => item.ProductoId == fixture.ProductoId);
        var subcategory = new Subcategoria
        {
            CategoriaId = product.CategoriaId,
            Nombre = "Subcategoría histórica",
            Descripcion = "Captura de prueba",
            Activa = true,
            FechaCreacion = DateTime.UtcNow,
            FechaModificacion = DateTime.UtcNow
        };
        fixture.Context.Subcategorias.Add(subcategory);
        await fixture.Context.SaveChangesAsync();
        product.SubcategoriaId = subcategory.SubcategoriaId;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var result = await fixture.Service.RegisterSaleAsync(CashSale(fixture.ProductoId));
        var original = await fixture.Context.DetalleVentas.AsNoTracking()
            .SingleAsync(item => item.VentaId == result.VentaId);

        Assert.Equal(subcategory.SubcategoriaId, original.SubcategoriaIdSnapshot);
        Assert.Equal("Subcategoría histórica", original.SubcategoriaNombreSnapshot);

        subcategory.Nombre = "Subcategoría renombrada";
        subcategory.Activa = false;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var stored = await fixture.Context.DetalleVentas.AsNoTracking()
            .SingleAsync(item => item.VentaId == result.VentaId);
        Assert.Equal(original.SubcategoriaIdSnapshot, stored.SubcategoriaIdSnapshot);
        Assert.Equal(original.SubcategoriaNombreSnapshot, stored.SubcategoriaNombreSnapshot);
    }

    [Fact]
    public async Task RegisterSaleAsync_DistinguishesKnownZeroCostFromUnknownHistoricalCost()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        var product = await fixture.Context.Productos.SingleAsync(p => p.ProductoId == fixture.ProductoId);
        product.PrecioCompraCentavos = 0;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var result = await fixture.Service.RegisterSaleAsync(CashSale(fixture.ProductoId));
        var line = await fixture.Context.DetalleVentas.AsNoTracking().SingleAsync(d => d.VentaId == result.VentaId);

        Assert.Equal(0, line.CostoUnitarioCentavos);
        Assert.Equal(OrigenCostoHistorico.PRODUCTO_ACTUAL, line.OrigenCosto);
    }

    [Fact]
    public async Task RegisterSaleAsync_ProductMasterChangesDoNotAlterStoredSnapshots()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        var result = await fixture.Service.RegisterSaleAsync(CashSale(fixture.ProductoId));
        var original = await fixture.Context.DetalleVentas.AsNoTracking().SingleAsync(d => d.VentaId == result.VentaId);
        var originalUserName = await fixture.Context.Ventas.AsNoTracking()
            .Where(sale => sale.VentaId == result.VentaId)
            .Select(sale => sale.UsuarioNombreSnapshot)
            .SingleAsync();

        var replacementCategory = new Categoria
        {
            Nombre = $"Reclasificada-{Guid.NewGuid():N}",
            Activa = true,
            FechaCreacion = DateTime.UtcNow
        };
        fixture.Context.Categorias.Add(replacementCategory);
        await fixture.Context.SaveChangesAsync();
        var product = await fixture.Context.Productos.SingleAsync(p => p.ProductoId == fixture.ProductoId);
        product.CodigoInterno = $"CAMBIADO-{Guid.NewGuid():N}";
        product.Nombre = "Nombre cambiado";
        product.CategoriaId = replacementCategory.CategoriaId;
        product.PrecioCompraCentavos = 7777;
        var userId = fixture.Session.UsuarioActual!.UsuarioId;
        var user = await fixture.Context.Usuarios.SingleAsync(candidate => candidate.UsuarioId == userId);
        user.NombreCompleto = "Empleado renombrado";
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var stored = await fixture.Context.DetalleVentas.AsNoTracking().SingleAsync(d => d.VentaId == result.VentaId);
        var storedUserName = await fixture.Context.Ventas.AsNoTracking()
            .Where(sale => sale.VentaId == result.VentaId)
            .Select(sale => sale.UsuarioNombreSnapshot)
            .SingleAsync();
        Assert.Equal(original.ProductoCodigoSnapshot, stored.ProductoCodigoSnapshot);
        Assert.Equal(original.ProductoNombreSnapshot, stored.ProductoNombreSnapshot);
        Assert.Equal(original.CategoriaIdSnapshot, stored.CategoriaIdSnapshot);
        Assert.Equal(original.CategoriaNombreSnapshot, stored.CategoriaNombreSnapshot);
        Assert.Equal(original.CostoUnitarioCentavos, stored.CostoUnitarioCentavos);
        Assert.Equal(originalUserName, storedUserName);
    }

    [Fact]
    public async Task RegisterSaleAsync_DistributesGeneralDiscountWithoutLosingCentavos()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        var second = await fixture.AddProductAsync("Segundo", 100);
        var third = await fixture.AddProductAsync("Tercero", 100);

        var result = await fixture.Service.RegisterSaleAsync(new RegisterSaleRequest(
            [
                new SaleLineRequest(second, 1),
                new SaleLineRequest(third, 1),
                new SaleLineRequest(fixture.ProductoId, 1)
            ],
            [new SalePaymentRequest(MetodoPago.EFECTIVO, 2349)],
            DescuentoGeneralCentavos: 1,
            DescuentoGeneralMotivo: "Ajuste de un centavo"));

        var allocations = await fixture.Context.DetalleVentas.AsNoTracking()
            .Where(line => line.VentaId == result.VentaId)
            .OrderBy(line => line.DetalleVentaId)
            .Select(line => line.DescuentoGeneralProporcionalCentavos)
            .ToListAsync();

        Assert.Equal(1, allocations.Sum(value => value ?? 0));
        Assert.Equal([0L, 0L, 1L], allocations.Select(value => value ?? -1).ToArray());
    }

    [Fact]
    public async Task RegisterSaleAsync_PersistsCashPaymentWithLineDiscount()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        var result = await fixture.Service.RegisterSaleAsync(new RegisterSaleRequest(
            [new SaleLineRequest(fixture.ProductoId, 1, 200, "Promo reze")],
            [new SalePaymentRequest(MetodoPago.EFECTIVO, 1950)]));

        Assert.Equal(2150, result.SubtotalCentavos);
        Assert.Equal(200, result.DescuentoCentavos);
        Assert.Equal(1950, result.TotalCentavos);
        Assert.Equal(1950, result.TotalPagadoCentavos);
        Assert.Equal(0, result.CambioCentavos);

        var sale = await fixture.Context.Ventas
            .AsNoTracking()
            .Include(v => v.Detalles)
            .SingleAsync(v => v.VentaId == result.VentaId);
        Assert.Null(sale.DescuentoMotivo);

        var line = Assert.Single(sale.Detalles);
        Assert.Equal(200, line.DescuentoCentavos);
        Assert.Equal("Promo reze", line.DescuentoMotivo);
        Assert.Equal(1950, line.TotalCentavos);
    }

    [Fact]
    public async Task RegisterSaleAsync_PersistsCashAndTransferPayment()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        var result = await fixture.Service.RegisterSaleAsync(new RegisterSaleRequest(
            [new SaleLineRequest(fixture.ProductoId, 1)],
            [
                new SalePaymentRequest(MetodoPago.EFECTIVO, 1500),
                new SalePaymentRequest(MetodoPago.TRANSFERENCIA, 650, "TRX-001")
            ]));

        Assert.Equal(2150, result.TotalCentavos);
        Assert.Equal(2, result.Payments.Count);
        Assert.Contains(result.Payments, p => p.MetodoPago == MetodoPago.EFECTIVO && p.MontoCentavos == 1500);
        Assert.Contains(result.Payments, p => p.MetodoPago == MetodoPago.TRANSFERENCIA && p.MontoCentavos == 650 && p.Referencia == "TRX-001");
    }

    [Fact]
    public async Task RegisterSaleAsync_PersistsCashPaymentWithChange()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        var result = await fixture.Service.RegisterSaleAsync(new RegisterSaleRequest(
            [new SaleLineRequest(fixture.ProductoId, 1)],
            [new SalePaymentRequest(MetodoPago.EFECTIVO, 3000)]));

        Assert.Equal(2150, result.TotalCentavos);
        Assert.Equal(3000, result.TotalPagadoCentavos);
        Assert.Equal(850, result.CambioCentavos);

        var storedSale = await fixture.Context.Ventas.AsNoTracking().SingleAsync(v => v.VentaId == result.VentaId);
        Assert.Equal(3000, storedSale.PagadoCentavos);
        Assert.Equal(850, storedSale.CambioCentavos);

        var storedPayment = await fixture.Context.PagosVenta.AsNoTracking().SingleAsync();
        Assert.Equal(MetodoPago.EFECTIVO, storedPayment.MetodoPago);
        Assert.Equal(2150, storedPayment.MontoCentavos);
    }

    [Fact]
    public async Task RegisterSaleAsync_AllowsSellingExactlyAvailableStock()
    {
        await using var fixture = await SaleFixture.CreateAsync(stockActual: 2);

        var result = await fixture.Service.RegisterSaleAsync(new RegisterSaleRequest(
            [new SaleLineRequest(fixture.ProductoId, 2)],
            [new SalePaymentRequest(MetodoPago.EFECTIVO, 4300)]));

        Assert.Equal(4300, result.TotalCentavos);
        Assert.Equal(2, Assert.Single(result.Lines).Cantidad);

        Assert.Equal(0, await fixture.GetPhysicalAsync(
            fixture.ProductoSkuId, UbicacionInventario.EXHIBICION));
    }

    [Fact]
    public async Task RegisterSaleAsync_BlocksQuantityAboveAvailableStock()
    {
        await using var fixture = await SaleFixture.CreateAsync(stockActual: 2);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RegisterSaleAsync(new RegisterSaleRequest(
                [new SaleLineRequest(fixture.ProductoId, 3)],
                [new SalePaymentRequest(MetodoPago.EFECTIVO, 6450)])));

        Assert.Contains("Stock insuficiente", exception.Message);
        Assert.Contains("Disponible: 2", exception.Message);
        await AssertPersistedCountsAsync(fixture.Context, ventas: 0, detalles: 0, pagos: 0);

        Assert.Equal(2, await fixture.GetPhysicalAsync(
            fixture.ProductoSkuId, UbicacionInventario.EXHIBICION));
    }

    [Fact]
    public async Task RegisterSaleAsync_BlocksProductWithoutStock()
    {
        await using var fixture = await SaleFixture.CreateAsync(stockActual: 0);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RegisterSaleAsync(new RegisterSaleRequest(
                [new SaleLineRequest(fixture.ProductoId, 1)],
                [new SalePaymentRequest(MetodoPago.EFECTIVO, 2150)])));

        Assert.Contains("Stock insuficiente", exception.Message);
        Assert.Contains("Disponible: 0", exception.Message);
        await AssertPersistedCountsAsync(fixture.Context, ventas: 0, detalles: 0, pagos: 0);
    }

    [Theory]
    [InlineData(ModoProducto.SIMPLE)]
    [InlineData(ModoProducto.SURTIDO)]
    public async Task RegisterSaleAsync_SimpleAndAssortedProductsUseTheirPrimarySku(ModoProducto mode)
    {
        await using var fixture = await SaleFixture.CreateAsync(stockActual: 2);
        await fixture.SetProductModeAsync(mode);

        var result = await fixture.Service.RegisterSaleAsync(CashSale(fixture.ProductoId));

        var line = Assert.Single(result.Lines);
        Assert.Equal(fixture.ProductoSkuId, line.ProductoSkuId);
        Assert.Equal(2150, line.PrecioUnitarioCentavos);
        Assert.Equal(1, await fixture.GetPhysicalAsync(
            fixture.ProductoSkuId,
            UbicacionInventario.EXHIBICION));
    }

    [Theory]
    [InlineData(ModoProducto.SIMPLE)]
    [InlineData(ModoProducto.SURTIDO)]
    public async Task RegisterSaleAsync_SimpleAndAssortedProductsRejectNonPrimarySku(ModoProducto mode)
    {
        await using var fixture = await SaleFixture.CreateAsync();
        await fixture.SetProductModeAsync(mode);
        var secondarySkuId = await fixture.AddSkuAsync(
            "REZE-SECUNDARIO",
            "Secundario",
            exhibitionStock: 2);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RegisterSaleAsync(new RegisterSaleRequest(
                [new SaleLineRequest(fixture.ProductoId, 1, ProductoSkuId: secondarySkuId)],
                [new SalePaymentRequest(MetodoPago.EFECTIVO, 2150)])));

        Assert.Contains("solo puede venderse mediante su SKU principal", exception.Message);
        await AssertPersistedCountsAsync(fixture.Context, ventas: 0, detalles: 0, pagos: 0);
    }

    [Fact]
    public async Task RegisterSaleAsync_VariantProductRequiresExactSku()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        await fixture.SetProductModeAsync(ModoProducto.POR_VARIANTES);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RegisterSaleAsync(CashSale(fixture.ProductoId)));

        Assert.Contains("Debe seleccionar el SKU o diseño exacto", exception.Message);
        await AssertPersistedCountsAsync(fixture.Context, ventas: 0, detalles: 0, pagos: 0);
    }

    [Fact]
    public async Task RegisterSaleAsync_VariantProductPersistsExactSkuAndEffectiveSnapshots()
    {
        await using var fixture = await SaleFixture.CreateAsync(stockActual: 3);
        await fixture.SetProductModeAsync(ModoProducto.POR_VARIANTES);
        var variantSkuId = await fixture.AddSkuAsync(
            "REZE-AZUL",
            "Azul",
            exhibitionStock: 2,
            priceOverrideCentavos: 2750,
            costOverrideCentavos: 1250);

        var result = await fixture.Service.RegisterSaleAsync(new RegisterSaleRequest(
            [new SaleLineRequest(fixture.ProductoId, 1, ProductoSkuId: variantSkuId)],
            [new SalePaymentRequest(MetodoPago.EFECTIVO, 2750)]));

        var resultLine = Assert.Single(result.Lines);
        Assert.Equal(variantSkuId, resultLine.ProductoSkuId);
        Assert.Equal("REZE-AZUL", resultLine.CodigoSku);
        Assert.Equal("Azul", resultLine.NombreVariante);
        Assert.Equal(2750, resultLine.PrecioUnitarioCentavos);

        var storedLine = await fixture.Context.DetalleVentas
            .AsNoTracking()
            .SingleAsync(line => line.VentaId == result.VentaId);
        Assert.Equal(variantSkuId, storedLine.ProductoSkuId);
        Assert.Equal("REZE-AZUL", storedLine.CodigoSkuSnapshot);
        Assert.Equal("Azul", storedLine.NombreVarianteSnapshot);
        Assert.Equal(2750, storedLine.PrecioUnitarioCentavos);
        Assert.Equal(1250, storedLine.CostoUnitarioCentavos);
        Assert.Equal(OrigenCostoHistorico.SKU_OVERRIDE, storedLine.OrigenCosto);
        Assert.Equal(1, await fixture.GetPhysicalAsync(variantSkuId, UbicacionInventario.EXHIBICION));
        Assert.Equal(3, await fixture.GetPhysicalAsync(
            fixture.ProductoSkuId,
            UbicacionInventario.EXHIBICION));
    }

    [Fact]
    public async Task RegisterSaleAsync_BlocksInactiveSku()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        await fixture.SetProductModeAsync(ModoProducto.POR_VARIANTES);
        var inactiveSkuId = await fixture.AddSkuAsync(
            "REZE-INACTIVO",
            "Inactivo",
            exhibitionStock: 2,
            active: false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RegisterSaleAsync(new RegisterSaleRequest(
                [new SaleLineRequest(fixture.ProductoId, 1, ProductoSkuId: inactiveSkuId)],
                [new SalePaymentRequest(MetodoPago.EFECTIVO, 2150)])));

        Assert.Contains("está inactivo", exception.Message);
        Assert.Equal(2, await fixture.GetPhysicalAsync(inactiveSkuId, UbicacionInventario.EXHIBICION));
        await AssertPersistedCountsAsync(fixture.Context, ventas: 0, detalles: 0, pagos: 0);
    }

    [Fact]
    public async Task RegisterSaleAsync_DoesNotConsumeWarehouseWhenExhibitionIsEmpty()
    {
        await using var fixture = await SaleFixture.CreateAsync(stockActual: 0);
        await fixture.SetInventoryAsync(
            fixture.ProductoSkuId,
            exhibitionPhysical: 0,
            warehousePhysical: 5);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RegisterSaleAsync(CashSale(fixture.ProductoId)));

        Assert.Contains("Stock insuficiente en Exhibición", exception.Message);
        Assert.Contains("En Bodega: 5", exception.Message);
        Assert.Equal(0, await fixture.GetPhysicalAsync(
            fixture.ProductoSkuId,
            UbicacionInventario.EXHIBICION));
        Assert.Equal(5, await fixture.GetPhysicalAsync(
            fixture.ProductoSkuId,
            UbicacionInventario.BODEGA));
        Assert.Empty(await fixture.Context.MovimientosInventarioUbicacion.ToListAsync());
    }

    [Fact]
    public async Task RegisterSaleAsync_RespectsReservedExhibitionStock()
    {
        await using var fixture = await SaleFixture.CreateAsync(stockActual: 3);
        await fixture.SetInventoryAsync(
            fixture.ProductoSkuId,
            exhibitionPhysical: 3,
            warehousePhysical: 0,
            exhibitionReserved: 2);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RegisterSaleAsync(new RegisterSaleRequest(
                [new SaleLineRequest(fixture.ProductoId, 2)],
                [new SalePaymentRequest(MetodoPago.EFECTIVO, 4300)])));

        Assert.Contains("Disponible: 1", exception.Message);

        await fixture.Service.RegisterSaleAsync(CashSale(fixture.ProductoId));

        var exhibition = await fixture.GetBalanceAsync(
            fixture.ProductoSkuId,
            UbicacionInventario.EXHIBICION);
        Assert.Equal(2, exhibition.ExistenciaFisica);
        Assert.Equal(2, exhibition.Reservado);
        Assert.Equal(0, exhibition.Disponible);
    }

    [Fact]
    public async Task RegisterSaleAsync_TwoLinesCanConsumeTheLastExhibitionUnitsExactly()
    {
        await using var fixture = await SaleFixture.CreateAsync(stockActual: 2);

        var result = await fixture.Service.RegisterSaleAsync(new RegisterSaleRequest(
            [
                new SaleLineRequest(fixture.ProductoId, 1, ProductoSkuId: fixture.ProductoSkuId),
                new SaleLineRequest(fixture.ProductoId, 1, ProductoSkuId: fixture.ProductoSkuId)
            ],
            [new SalePaymentRequest(MetodoPago.EFECTIVO, 4300)]));

        Assert.Equal(2, result.Lines.Count);
        Assert.All(result.Lines, line => Assert.Equal(fixture.ProductoSkuId, line.ProductoSkuId));
        Assert.Equal(0, await fixture.GetPhysicalAsync(
            fixture.ProductoSkuId,
            UbicacionInventario.EXHIBICION));
        Assert.Equal(2, await fixture.Context.Productos
            .AsNoTracking()
            .Where(product => product.ProductoId == fixture.ProductoId)
            .Select(product => product.StockActual)
            .SingleAsync());
        Assert.Equal(2, await fixture.Context.MovimientosInventarioUbicacion.CountAsync());
    }

    [Fact]
    public async Task RegisterSaleAsync_PersistsCashTransferAndBitcoinPayment()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        var result = await fixture.Service.RegisterSaleAsync(new RegisterSaleRequest(
            [new SaleLineRequest(fixture.ProductoId, 1)],
            [
                new SalePaymentRequest(MetodoPago.EFECTIVO, 1500),
                new SalePaymentRequest(MetodoPago.TRANSFERENCIA, 100, "TRX-002"),
                new SalePaymentRequest(MetodoPago.BITCOIN, 550, "BTC-002")
            ]));

        Assert.Equal(2150, result.TotalCentavos);
        Assert.Equal(3, result.Payments.Count);
        Assert.Contains(result.Payments, p => p.MetodoPago == MetodoPago.BITCOIN && p.MontoCentavos == 550 && p.Referencia == "BTC-002");

        var storedBitcoinPayment = await fixture.Context.PagosVenta
            .AsNoTracking()
            .SingleAsync(p => p.MetodoPago == MetodoPago.BITCOIN);
        Assert.Equal("BTC-002", storedBitcoinPayment.Referencia);
    }

    [Fact]
    public async Task RegisterSaleAsync_RejectsBitcoinWhenDisabledByBusinessConfiguration()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        await fixture.SetBitcoinEnabledAsync(false);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.RegisterSaleAsync(
            new RegisterSaleRequest(
                [new SaleLineRequest(fixture.ProductoId, 1)],
                [new SalePaymentRequest(MetodoPago.BITCOIN, 2150, "BTC-DISABLED")])));

        Assert.Contains("Bitcoin esta deshabilitado", error.Message);
        Assert.Empty(await fixture.Context.Ventas.ToListAsync());
    }

    [Fact]
    public async Task RegisterSaleAsync_PersistsLineDiscountWithMixedPayment()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        var result = await fixture.Service.RegisterSaleAsync(new RegisterSaleRequest(
            [new SaleLineRequest(fixture.ProductoId, 1, 200, "Descuento por producto reze")],
            [
                new SalePaymentRequest(MetodoPago.EFECTIVO, 1500),
                new SalePaymentRequest(MetodoPago.TRANSFERENCIA, 100, "TRX-REZE"),
                new SalePaymentRequest(MetodoPago.BITCOIN, 350, "BTC-REZE")
            ]));

        Assert.Equal(2150, result.SubtotalCentavos);
        Assert.Equal(200, result.DescuentoCentavos);
        Assert.Equal(1950, result.TotalCentavos);
        Assert.Equal(1950, result.TotalPagadoCentavos);
        Assert.Equal(0, result.CambioCentavos);

        Assert.Contains(result.Payments, p => p.MetodoPago == MetodoPago.TRANSFERENCIA && p.Referencia == "TRX-REZE");
        Assert.Contains(result.Payments, p => p.MetodoPago == MetodoPago.BITCOIN && p.Referencia == "BTC-REZE");

        var sale = await fixture.Context.Ventas
            .AsNoTracking()
            .Include(v => v.Detalles)
            .Include(v => v.Pagos)
            .SingleAsync(v => v.VentaId == result.VentaId);

        Assert.Null(sale.DescuentoMotivo);
        Assert.Equal(200, Assert.Single(sale.Detalles).DescuentoCentavos);
        Assert.Equal(3, sale.Pagos.Count);
    }

    [Fact]
    public async Task RegisterSaleAsync_RollsBackAllRowsAndStockWhenSaveFailsBeforeCommit()
    {
        await using var fixture = await SaleFixture.CreateAsync(auditServiceFactory: _ => new ThrowingAuditService());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RegisterSaleAsync(new RegisterSaleRequest(
                [new SaleLineRequest(fixture.ProductoId, 1)],
                [new SalePaymentRequest(MetodoPago.EFECTIVO, 2150)])));
        Assert.Equal("Fallo simulado al guardar auditoria.", exception.Message);

        fixture.Context.ChangeTracker.Clear();

        await AssertPersistedCountsAsync(fixture.Context, ventas: 0, detalles: 0, pagos: 0);
        Assert.Equal(0, await fixture.Context.MovimientosInventario.CountAsync());

        var product = await fixture.Context.Productos.AsNoTracking().SingleAsync(p => p.ProductoId == fixture.ProductoId);
        Assert.Equal(10, product.StockActual);
    }

    [Theory]
    [InlineData(RolUsuario.ADMIN)]
    [InlineData(RolUsuario.EMPLEADO)]
    public async Task RegisterSaleAsync_FactoryBacked_PersistsSaleAndAuditForCurrentRole(RolUsuario role)
    {
        await using var fixture = await SaleFixture.CreateAsync(factoryBacked: true, role: role);

        var result = await fixture.Service.RegisterSaleAsync(CashSale(fixture.ProductoId));

        fixture.Context.ChangeTracker.Clear();
        await AssertPersistedCountsAsync(fixture.Context, ventas: 1, detalles: 1, pagos: 1);
        Assert.Equal(1, await fixture.Context.MovimientosInventario.CountAsync(m => m.VentaId == result.VentaId));
        Assert.Equal(1, await fixture.Context.Bitacora.CountAsync(b =>
            b.TipoAccion == TipoBitacora.VENTA_REALIZADA && b.EntidadId == result.VentaId));
        Assert.Equal(9, await fixture.GetPhysicalAsync(
            fixture.ProductoSkuId, UbicacionInventario.EXHIBICION));
    }

    [Fact]
    public async Task RegisterSaleAsync_FactoryBacked_RollsBackAndRetriesWithFreshContext()
    {
        await using var fixture = await SaleFixture.CreateAsync(factoryBacked: true);
        await fixture.Context.Database.ExecuteSqlRawAsync(
            """
            CREATE TRIGGER fail_sale_audit
            BEFORE INSERT ON bitacora
            WHEN NEW.tipo_accion = 'VENTA_REALIZADA'
            BEGIN
                SELECT RAISE(ABORT, 'simulated sale audit failure');
            END;
            """);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            fixture.Service.RegisterSaleAsync(CashSale(fixture.ProductoId)));

        fixture.Context.ChangeTracker.Clear();
        await AssertPersistedCountsAsync(fixture.Context, ventas: 0, detalles: 0, pagos: 0);
        Assert.Equal(0, await fixture.Context.MovimientosInventario.CountAsync());
        Assert.Equal(0, await fixture.Context.Bitacora.CountAsync(b => b.TipoAccion == TipoBitacora.VENTA_REALIZADA));
        Assert.Equal(10, await fixture.Context.Productos
            .Where(p => p.ProductoId == fixture.ProductoId)
            .Select(p => p.StockActual)
            .SingleAsync());

        await fixture.Context.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_sale_audit;");
        var retry = await fixture.Service.RegisterSaleAsync(CashSale(fixture.ProductoId));

        fixture.Context.ChangeTracker.Clear();
        await AssertPersistedCountsAsync(fixture.Context, ventas: 1, detalles: 1, pagos: 1);
        Assert.Equal(1, await fixture.Context.Bitacora.CountAsync(b =>
            b.TipoAccion == TipoBitacora.VENTA_REALIZADA && b.EntidadId == retry.VentaId));
        Assert.Equal(9, await fixture.GetPhysicalAsync(
            fixture.ProductoSkuId, UbicacionInventario.EXHIBICION));
    }

    [Fact]
    public async Task RegisterSaleAsync_FactoryBacked_AllowsTwentySequentialSales()
    {
        await using var fixture = await SaleFixture.CreateAsync(factoryBacked: true, stockActual: 25);

        for (var index = 0; index < 20; index++)
        {
            await fixture.Service.RegisterSaleAsync(CashSale(fixture.ProductoId));
        }

        fixture.Context.ChangeTracker.Clear();
        await AssertPersistedCountsAsync(fixture.Context, ventas: 20, detalles: 20, pagos: 20);
        Assert.Equal(20, await fixture.Context.MovimientosInventario.CountAsync());
        Assert.Equal(20, await fixture.Context.Bitacora.CountAsync(b => b.TipoAccion == TipoBitacora.VENTA_REALIZADA));
        Assert.Equal(5, await fixture.GetPhysicalAsync(
            fixture.ProductoSkuId, UbicacionInventario.EXHIBICION));
    }

    [Fact]
    public async Task RegisterSaleAsync_FactoryBacked_WorksAfterLogoutAndLogin()
    {
        await using var fixture = await SaleFixture.CreateAsync(factoryBacked: true);
        var user = fixture.Session.UsuarioActual!;

        await fixture.Service.RegisterSaleAsync(CashSale(fixture.ProductoId));
        fixture.Session.CerrarSesion();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RegisterSaleAsync(CashSale(fixture.ProductoId)));
        fixture.Session.IniciarSesion(user);
        await fixture.Service.RegisterSaleAsync(CashSale(fixture.ProductoId));

        fixture.Context.ChangeTracker.Clear();
        await AssertPersistedCountsAsync(fixture.Context, ventas: 2, detalles: 2, pagos: 2);
        Assert.Equal(2, await fixture.Context.Bitacora.CountAsync(b => b.TipoAccion == TipoBitacora.VENTA_REALIZADA));
    }

    private static RegisterSaleRequest CashSale(int productId) => new(
        [new SaleLineRequest(productId, 1)],
        [new SalePaymentRequest(MetodoPago.EFECTIVO, 2150)]);

    [Fact]
    public async Task RegisterSaleAsync_PersistsFullCreditSaleWithoutImmediatePayment()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        var result = await fixture.Service.RegisterSaleAsync(new RegisterSaleRequest(
            [new SaleLineRequest(fixture.ProductoId, 1)],
            [],
            ClienteId: fixture.CustomerId,
            Credit: new CreditSaleRequest(
                fixture.CustomerId,
                7,
                [new CreditSaleLineRequest(fixture.ProductoId, 1)])));

        Assert.NotNull(result.Credit);
        Assert.Equal(2150, result.TotalCentavos);
        Assert.Equal(0, result.TotalPagadoCentavos);
        Assert.Equal(2150, result.Credit!.CapitalFinanciadoCentavos);
        Assert.Equal(2150, result.Credit.TotalCuentaCentavos);
        Assert.Empty(result.Payments);

        var account = await fixture.Context.CuentasPorCobrar
            .AsNoTracking()
            .Include(c => c.Detalles)
            .SingleAsync();
        Assert.Equal(result.VentaId, account.VentaId);
        Assert.Equal(fixture.CustomerId, account.ClienteId);
        Assert.Equal(EstadoCuentaPorCobrar.ABIERTA, account.Estado);
        Assert.Equal(2150, account.SaldoActualCentavos);
        Assert.Equal(1, Assert.Single(account.Detalles).CantidadFinanciada);
    }

    [Fact]
    public async Task RegisterSaleAsync_PersistsCreditSaleWithInitialCashPayment()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        var result = await fixture.Service.RegisterSaleAsync(new RegisterSaleRequest(
            [new SaleLineRequest(fixture.ProductoId, 1)],
            [new SalePaymentRequest(MetodoPago.EFECTIVO, 500)],
            ClienteId: fixture.CustomerId,
            Credit: new CreditSaleRequest(
                fixture.CustomerId,
                7,
                [new CreditSaleLineRequest(fixture.ProductoId, 1)])));

        Assert.NotNull(result.Credit);
        Assert.Equal(500, result.TotalPagadoCentavos);
        Assert.Equal(1650, result.Credit!.CapitalFinanciadoCentavos);
        Assert.Equal(1650, result.Credit.TotalCuentaCentavos);
        Assert.Equal(500, Assert.Single(result.Payments).MontoCentavos);
    }

    [Fact]
    public async Task RegisterSaleAsync_BlocksFinalConsumerCredit()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RegisterSaleAsync(new RegisterSaleRequest(
                [new SaleLineRequest(fixture.ProductoId, 1)],
                [],
                Credit: new CreditSaleRequest(
                    fixture.CustomerId,
                    7,
                    [new CreditSaleLineRequest(fixture.ProductoId, 1)]))));

        Assert.Contains("cliente real", exception.Message);
        await AssertPersistedCountsAsync(fixture.Context, ventas: 0, detalles: 0, pagos: 0);
    }

    [Fact]
    public async Task RegisterSaleAsync_BlocksInsufficientMandatoryCashForPartialCredit()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RegisterSaleAsync(new RegisterSaleRequest(
                [new SaleLineRequest(fixture.ProductoId, 2)],
                [new SalePaymentRequest(MetodoPago.EFECTIVO, 1000)],
                ClienteId: fixture.CustomerId,
                Credit: new CreditSaleRequest(
                    fixture.CustomerId,
                    7,
                    [new CreditSaleLineRequest(fixture.ProductoId, 1)]))));

        Assert.Contains("parte de contado", exception.Message);
        await AssertPersistedCountsAsync(fixture.Context, ventas: 0, detalles: 0, pagos: 0);
    }

    [Fact]
    public async Task RegisterSaleAsync_AppliesSimpleInterestOnce()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        await fixture.SetDefaultInterestAsync("10");

        var result = await fixture.Service.RegisterSaleAsync(new RegisterSaleRequest(
            [new SaleLineRequest(fixture.ProductoId, 1)],
            [],
            ClienteId: fixture.CustomerId,
            Credit: new CreditSaleRequest(
                fixture.CustomerId,
                7,
                [new CreditSaleLineRequest(fixture.ProductoId, 1)])));

        Assert.NotNull(result.Credit);
        Assert.Equal(2150, result.Credit!.CapitalFinanciadoCentavos);
        Assert.Equal(215, result.Credit.InteresCalculadoCentavos);
        Assert.Equal(2365, result.Credit.TotalCuentaCentavos);
    }

    [Fact]
    public async Task RegisterSaleAsync_BlocksCreditWhenCustomerPolicyDisabled()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        await fixture.UpdateCustomerCreditPolicyAsync(customer =>
        {
            customer.CreditoHabilitado = false;
            customer.CreditoBloqueoMotivo = "Mora externa";
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RegisterSaleAsync(new RegisterSaleRequest(
                [new SaleLineRequest(fixture.ProductoId, 1)],
                [],
                ClienteId: fixture.CustomerId,
                Credit: new CreditSaleRequest(
                    fixture.CustomerId,
                    7,
                    [new CreditSaleLineRequest(fixture.ProductoId, 1)]))));

        Assert.Contains("bloqueado", exception.Message);
        await AssertPersistedCountsAsync(fixture.Context, ventas: 0, detalles: 0, pagos: 0);
    }

    [Fact]
    public async Task RegisterSaleAsync_BlocksCreditWhenProjectedDebtExceedsLimit()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        await fixture.UpdateCustomerCreditPolicyAsync(customer => customer.CreditoLimiteCentavos = 2000);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RegisterSaleAsync(new RegisterSaleRequest(
                [new SaleLineRequest(fixture.ProductoId, 1)],
                [],
                ClienteId: fixture.CustomerId,
                Credit: new CreditSaleRequest(
                    fixture.CustomerId,
                    7,
                    [new CreditSaleLineRequest(fixture.ProductoId, 1)]))));

        Assert.Contains("limite", exception.Message);
        await AssertPersistedCountsAsync(fixture.Context, ventas: 0, detalles: 0, pagos: 0);
    }

    [Fact]
    public async Task RegisterSaleAsync_BlocksCreditWhenMaximumActiveAccountsReached()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        await fixture.UpdateCustomerCreditPolicyAsync(customer => customer.CreditoMaximoCuentasActivas = 1);

        await fixture.Service.RegisterSaleAsync(new RegisterSaleRequest(
            [new SaleLineRequest(fixture.ProductoId, 1)],
            [],
            ClienteId: fixture.CustomerId,
            Credit: new CreditSaleRequest(
                fixture.CustomerId,
                7,
                [new CreditSaleLineRequest(fixture.ProductoId, 1)])));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RegisterSaleAsync(new RegisterSaleRequest(
                [new SaleLineRequest(fixture.ProductoId, 1)],
                [],
                ClienteId: fixture.CustomerId,
                Credit: new CreditSaleRequest(
                    fixture.CustomerId,
                    7,
                    [new CreditSaleLineRequest(fixture.ProductoId, 1)]))));

        Assert.Contains("maximo de cuentas", exception.Message);
        Assert.Equal(1, await fixture.Context.CuentasPorCobrar.CountAsync());
    }

    [Fact]
    public async Task RegisterSaleAsync_ProrratesGeneralDiscountForPartialQuantityCredit()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        var result = await fixture.Service.RegisterSaleAsync(new RegisterSaleRequest(
            [new SaleLineRequest(fixture.ProductoId, 2)],
            [new SalePaymentRequest(MetodoPago.EFECTIVO, 2000)],
            DescuentoGeneralCentavos: 300,
            DescuentoGeneralMotivo: "Promo general",
            ClienteId: fixture.CustomerId,
            Credit: new CreditSaleRequest(
                fixture.CustomerId,
                7,
                [new CreditSaleLineRequest(fixture.ProductoId, 1)])));

        Assert.NotNull(result.Credit);
        Assert.Equal(4000, result.TotalCentavos);
        Assert.Equal(2000, result.TotalPagadoCentavos);
        Assert.Equal(2000, result.Credit!.CapitalFinanciadoCentavos);

        var creditLine = await fixture.Context.CuentasPorCobrarDetalle.AsNoTracking().SingleAsync();
        Assert.Equal(1, creditLine.CantidadFinanciada);
        Assert.Equal(150, creditLine.DescuentoGeneralProporcionalCentavos);
        Assert.Equal(2000, creditLine.SubtotalFinanciadoCentavos);
        Assert.Equal(2000, creditLine.CapitalPendienteCentavos);
    }

    [Fact]
    public async Task RegisterSaleAsync_PersistsThirtyThreeDollarSaleWithNineCashAndTwentyFourCredit()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        var azucareraId = await fixture.AddProductAsync("Azucarera", 2400);
        var bandasId = await fixture.AddProductAsync("Bandas", 900);

        var result = await fixture.Service.RegisterSaleAsync(new RegisterSaleRequest(
            [
                new SaleLineRequest(azucareraId, 1),
                new SaleLineRequest(bandasId, 1)
            ],
            [new SalePaymentRequest(MetodoPago.EFECTIVO, 900)],
            ClienteId: fixture.CustomerId,
            Credit: new CreditSaleRequest(
                fixture.CustomerId,
                7,
                [new CreditSaleLineRequest(azucareraId, 1)])));

        Assert.Equal(3300, result.TotalCentavos);
        Assert.Equal(900, result.TotalPagadoCentavos);
        Assert.Equal(0, result.CambioCentavos);
        Assert.NotNull(result.Credit);
        Assert.Equal(2400, result.Credit!.CapitalFinanciadoCentavos);
        Assert.Equal(2400, result.Credit.TotalCuentaCentavos);

        Assert.Equal(1, await fixture.Context.Ventas.CountAsync());
        Assert.Equal(2, await fixture.Context.DetalleVentas.CountAsync());
        var payment = await fixture.Context.PagosVenta.AsNoTracking().SingleAsync();
        Assert.Equal(MetodoPago.EFECTIVO, payment.MetodoPago);
        Assert.Equal(900, payment.MontoCentavos);

        var account = await fixture.Context.CuentasPorCobrar
            .AsNoTracking()
            .Include(c => c.Detalles)
            .SingleAsync();
        Assert.Equal(result.VentaId, account.VentaId);
        Assert.Equal(2400, account.SaldoActualCentavos);
        var creditLine = Assert.Single(account.Detalles);
        Assert.Equal(azucareraId, creditLine.ProductoId);
        Assert.Equal("Azucarera", creditLine.ProductoNombreSnapshot);
        Assert.Equal(2400, creditLine.SubtotalFinanciadoCentavos);
    }

    private static async Task AssertPersistedCountsAsync(
        AppDbContext context,
        int ventas,
        int detalles,
        int pagos)
    {
        Assert.Equal(ventas, await context.Ventas.CountAsync());
        Assert.Equal(detalles, await context.DetalleVentas.CountAsync());
        Assert.Equal(pagos, await context.PagosVenta.CountAsync());
    }

    private sealed class SaleFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private SaleFixture(
            SqliteConnection connection,
            AppDbContext context,
            SaleService service,
            int productoId,
            int productoSkuId,
            int customerId,
            CreditService creditService,
            TestCurrentSession session)
        {
            _connection = connection;
            Context = context;
            Service = service;
            ProductoId = productoId;
            ProductoSkuId = productoSkuId;
            CustomerId = customerId;
            CreditService = creditService;
            Session = session;
        }

        public AppDbContext Context { get; }

        public SaleService Service { get; }

        public int ProductoId { get; }

        public int ProductoSkuId { get; }

        public int CustomerId { get; }

        public CreditService CreditService { get; }

        public TestCurrentSession Session { get; }

        public static async Task<SaleFixture> CreateAsync(
            Func<AppDbContext, IAuditService>? auditServiceFactory = null,
            int stockActual = 10,
            bool factoryBacked = false,
            RolUsuario role = RolUsuario.EMPLEADO)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;

            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var usuario = new Usuario
            {
                NombreCompleto = "Empleado de pruebas",
                NombreUsuario = $"empleado-{Guid.NewGuid():N}",
                PasswordHash = "hash",
                Rol = role,
                Activo = true,
                FechaCreacion = DateTime.UtcNow
            };

            var categoria = new Categoria
            {
                Nombre = $"Categoria-{Guid.NewGuid():N}",
                Activa = true,
                FechaCreacion = DateTime.UtcNow
            };

            context.Usuarios.Add(usuario);
            context.Categorias.Add(categoria);
            await context.SaveChangesAsync();

            var customer = new Cliente
            {
                CodigoCliente = $"CLI-{Guid.NewGuid():N}"[..12],
                NombreCompleto = "Cliente credito",
                NombreNormalizado = "cliente credito",
                Estado = EstadoCliente.ACTIVO,
                CreditoHabilitado = true,
                UsuarioCreacionId = usuario.UsuarioId,
                FechaCreacion = DateTime.UtcNow,
                FechaActualizacion = DateTime.UtcNow
            };

            var producto = new Producto
            {
                CodigoInterno = $"REZE-{Guid.NewGuid():N}",
                Nombre = "reze",
                CategoriaId = categoria.CategoriaId,
                PrecioCompraCentavos = 1000,
                PrecioVentaCentavos = 2150,
                StockActual = stockActual,
                StockMinimo = 1,
                Estado = EstadoProducto.ACTIVO,
                FechaCreacion = DateTime.UtcNow
            };

            var caja = new Caja
            {
                FechaOperacion = DateOnly.FromDateTime(DateTime.Now),
                FechaApertura = DateTime.UtcNow,
                MontoInicialCentavos = 0,
                MontoCierreCentavos = 0,
                Estado = EstadoCaja.ABIERTA,
                UsuarioAperturaId = usuario.UsuarioId
            };

            context.Clientes.Add(customer);
            context.Productos.Add(producto);
            context.Cajas.Add(caja);
            await context.SaveChangesAsync();

            var primarySku = new ProductoSku
            {
                ProductoId = producto.ProductoId,
                CodigoSku = producto.CodigoInterno,
                NombreVariante = "Principal",
                Activo = true,
                EsPrincipal = true
            };
            context.ProductoSkus.Add(primarySku);
            await context.SaveChangesAsync();
            context.InventarioSaldos.AddRange(CreateInventoryBalances(primarySku.ProductoSkuId, stockActual));
            await context.SaveChangesAsync();

            var currentSession = new TestCurrentSession(new UsuarioSesion(
                usuario.UsuarioId,
                usuario.NombreCompleto,
                usuario.NombreUsuario,
                usuario.Rol));
            var auditService = auditServiceFactory?.Invoke(context) ?? new AuditService(context);
            var permissionService = new EmployeePermissionService(context, currentSession, auditService);
            if (role == RolUsuario.EMPLEADO)
            {
                await permissionService.EnsureRecommendedPermissionsForEmployeeAsync(usuario.UsuarioId);
            }
            var creditPermissions = await context.UsuarioPermisos
                .Where(p =>
                    p.UsuarioId == usuario.UsuarioId &&
                    (p.Permiso == PermisoEmpleado.CLIENTES_ACCEDER ||
                        p.Permiso == PermisoEmpleado.CREDITO_REGISTRAR ||
                        p.Permiso == PermisoEmpleado.CUENTAS_COBRAR_VER))
                .ToListAsync();
            foreach (var permission in creditPermissions)
            {
                permission.Habilitado = true;
                permission.FechaActualizacion = DateTime.UtcNow;
            }

            await context.SaveChangesAsync();
            var creditService = new CreditService(context, currentSession, permissionService, auditService);
            var service = factoryBacked
                ? new SaleService(
                    new TestDbContextFactory(options),
                    currentSession,
                    auditService,
                    permissionService,
                    creditService)
                : new SaleService(context, currentSession, auditService, permissionService, creditService);

            return new SaleFixture(
                connection,
                context,
                service,
                producto.ProductoId,
                primarySku.ProductoSkuId,
                customer.ClienteId,
                creditService,
                currentSession);
        }

        public async Task SetDefaultInterestAsync(string value)
        {
            var config = await Context.ConfiguracionSistema
                .SingleOrDefaultAsync(c => c.Clave == "credito.interes_predeterminado_porcentaje");
            if (config is null)
            {
                config = new ConfiguracionSistema
                {
                    Clave = "credito.interes_predeterminado_porcentaje",
                    Descripcion = "Interes simple predeterminado para credito.",
                    FechaModificacion = DateTime.UtcNow
                };
                Context.ConfiguracionSistema.Add(config);
            }

            config.Valor = value;
            await Context.SaveChangesAsync();
        }

        public async Task SetBitcoinEnabledAsync(bool enabled)
        {
            var config = await Context.ConfiguracionSistema
                .SingleOrDefaultAsync(c => c.Clave == SystemConfigService.BitcoinEnabledKey);
            if (config is null)
            {
                config = new ConfiguracionSistema
                {
                    Clave = SystemConfigService.BitcoinEnabledKey,
                    Descripcion = "Permite usar Bitcoin en operaciones futuras.",
                    FechaModificacion = DateTime.UtcNow
                };
                Context.ConfiguracionSistema.Add(config);
            }

            config.Valor = enabled ? "true" : "false";
            await Context.SaveChangesAsync();
        }

        public async Task UpdateCustomerCreditPolicyAsync(Action<Cliente> configure)
        {
            var customer = await Context.Clientes.SingleAsync(c => c.ClienteId == CustomerId);
            configure(customer);
            customer.FechaActualizacion = DateTime.UtcNow;
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
        }

        public async Task SetProductModeAsync(ModoProducto mode)
        {
            var product = await Context.Productos.SingleAsync(item => item.ProductoId == ProductoId);
            product.Modo = mode;
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
        }

        public async Task<int> AddSkuAsync(
            string code,
            string variantName,
            int exhibitionStock,
            int warehouseStock = 0,
            bool active = true,
            long? priceOverrideCentavos = null,
            long? costOverrideCentavos = null)
        {
            var sku = new ProductoSku
            {
                ProductoId = ProductoId,
                CodigoSku = code,
                NombreVariante = variantName,
                Activo = active,
                EsPrincipal = false,
                PrecioVentaOverrideCentavos = priceOverrideCentavos,
                CostoOverrideCentavos = costOverrideCentavos
            };
            Context.ProductoSkus.Add(sku);
            await Context.SaveChangesAsync();

            var balances = CreateInventoryBalances(sku.ProductoSkuId, exhibitionStock);
            balances.Single(balance => balance.Ubicacion == UbicacionInventario.BODEGA)
                .ExistenciaFisica = warehouseStock;
            Context.InventarioSaldos.AddRange(balances);
            await Context.SaveChangesAsync();
            await RefreshProductStockProjectionAsync();
            Context.ChangeTracker.Clear();
            return sku.ProductoSkuId;
        }

        public async Task SetInventoryAsync(
            int skuId,
            int exhibitionPhysical,
            int warehousePhysical,
            int exhibitionReserved = 0)
        {
            var balances = await Context.InventarioSaldos
                .Where(balance => balance.ProductoSkuId == skuId)
                .ToListAsync();
            var exhibition = balances.Single(balance =>
                balance.Ubicacion == UbicacionInventario.EXHIBICION);
            exhibition.ExistenciaFisica = exhibitionPhysical;
            exhibition.Reservado = exhibitionReserved;
            exhibition.Version++;

            var warehouse = balances.Single(balance =>
                balance.Ubicacion == UbicacionInventario.BODEGA);
            warehouse.ExistenciaFisica = warehousePhysical;
            warehouse.Reservado = 0;
            warehouse.Version++;

            var unassigned = balances.Single(balance =>
                balance.Ubicacion == UbicacionInventario.SIN_ASIGNAR);
            unassigned.ExistenciaFisica = 0;
            unassigned.Reservado = 0;
            unassigned.Version++;

            await Context.SaveChangesAsync();
            await RefreshProductStockProjectionAsync();
            Context.ChangeTracker.Clear();
        }

        public Task<InventarioSaldo> GetBalanceAsync(int skuId, UbicacionInventario location) =>
            Context.InventarioSaldos
                .AsNoTracking()
                .SingleAsync(balance =>
                    balance.ProductoSkuId == skuId &&
                    balance.Ubicacion == location);

        public async Task<int> GetPhysicalAsync(int skuId, UbicacionInventario location) =>
            (await GetBalanceAsync(skuId, location)).ExistenciaFisica;

        public async Task<int> AddProductAsync(string name, long priceCentavos, int stockActual = 10)
        {
            var categoryId = await Context.Productos
                .AsNoTracking()
                .Where(p => p.ProductoId == ProductoId)
                .Select(p => p.CategoriaId)
                .SingleAsync();
            var product = new Producto
            {
                CodigoInterno = $"{name.ToUpperInvariant()}-{Guid.NewGuid():N}",
                Nombre = name,
                CategoriaId = categoryId,
                PrecioCompraCentavos = Math.Max(1, priceCentavos / 2),
                PrecioVentaCentavos = priceCentavos,
                StockActual = stockActual,
                StockMinimo = 1,
                Estado = EstadoProducto.ACTIVO,
                FechaCreacion = DateTime.UtcNow
            };

            Context.Productos.Add(product);
            await Context.SaveChangesAsync();
            var primarySku = new ProductoSku
            {
                ProductoId = product.ProductoId,
                CodigoSku = product.CodigoInterno,
                NombreVariante = "Principal",
                Activo = true,
                EsPrincipal = true
            };
            Context.ProductoSkus.Add(primarySku);
            await Context.SaveChangesAsync();
            Context.InventarioSaldos.AddRange(CreateInventoryBalances(primarySku.ProductoSkuId, stockActual));
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
            return product.ProductoId;
        }

        private static InventarioSaldo[] CreateInventoryBalances(int skuId, int exhibitionStock) =>
        [
            new InventarioSaldo
            {
                ProductoSkuId = skuId,
                Ubicacion = UbicacionInventario.EXHIBICION,
                ExistenciaFisica = exhibitionStock,
                Reservado = 0,
                Version = 1
            },
            new InventarioSaldo
            {
                ProductoSkuId = skuId,
                Ubicacion = UbicacionInventario.BODEGA,
                ExistenciaFisica = 0,
                Reservado = 0,
                Version = 1
            },
            new InventarioSaldo
            {
                ProductoSkuId = skuId,
                Ubicacion = UbicacionInventario.SIN_ASIGNAR,
                ExistenciaFisica = 0,
                Reservado = 0,
                Version = 1
            }
        ];

        private async Task RefreshProductStockProjectionAsync()
        {
            var stock = await Context.InventarioSaldos
                .Where(balance => balance.ProductoSku.ProductoId == ProductoId)
                .SumAsync(balance => balance.ExistenciaFisica);
            var product = await Context.Productos.SingleAsync(item => item.ProductoId == ProductoId);
            product.StockActual = stock;
            await Context.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class TestCurrentSession : ICurrentSession
    {
        public TestCurrentSession(UsuarioSesion usuario)
        {
            UsuarioActual = usuario;
        }

        public UsuarioSesion? UsuarioActual { get; private set; }

        public bool EstaAutenticado => UsuarioActual is not null;

        public bool EsAdmin => TieneRol(RolUsuario.ADMIN);

        public bool TieneRol(RolUsuario rol)
        {
            return UsuarioActual?.Rol == rol;
        }

        public void IniciarSesion(UsuarioSesion usuario)
        {
            UsuarioActual = usuario;
        }

        public void CerrarSesion()
        {
            UsuarioActual = null;
        }
    }

    private sealed class ThrowingAuditService : IAuditService
    {
        public Task RegistrarAsync(AuditEntryRequest request, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Fallo simulado al guardar auditoria.");
        }
    }
}
