using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public class SaleCorrectionServiceTests
{
    [Fact]
    public async Task ChangeSamePrice_RegistersExchangeWithoutMoneyMovement()
    {
        await using var fixture = await CorrectionFixture.CreateAsync(RolUsuario.EMPLEADO);
        var sale = await fixture.CreateSaleAsync(quantity: 1);

        var result = await fixture.ChangeService.RegisterChangeAsync(new RegisterSaleChangeRequest(
            sale.VentaId,
            sale.Lines.Single().DetalleVentaId,
            1,
            fixture.SamePriceProductId,
            CondicionProductoDevuelto.BUENO,
            true,
            "Cambio por talla"));

        Assert.Equal(0, result.DiferenciaCentavos);
        Assert.Null(result.PagoCorreccionVentaId);
        Assert.Null(result.ReembolsoId);
        Assert.Equal(1, await fixture.Context.DetallesCambioVenta.CountAsync());
        Assert.Equal(0, await fixture.Context.PagosCorreccionVenta.CountAsync());
        Assert.Equal(0, await fixture.Context.Reembolsos.CountAsync());
    }

    [Fact]
    public async Task Change_PersistsIndependentReplacementProductSnapshot()
    {
        await using var fixture = await CorrectionFixture.CreateAsync(RolUsuario.ADMIN);
        var sale = await fixture.CreateSaleAsync(quantity: 1);

        await fixture.ChangeService.RegisterChangeAsync(new RegisterSaleChangeRequest(
            sale.VentaId,
            sale.Lines.Single().DetalleVentaId,
            1,
            fixture.SamePriceProductId,
            CondicionProductoDevuelto.BUENO,
            true,
            "Cambio con snapshot"));

        var snapshot = await fixture.Context.DetallesCambioVenta.AsNoTracking().SingleAsync();
        Assert.Equal("Mismo precio", snapshot.ProductoNombreSnapshot);
        Assert.StartsWith("MISMO-PRECIO-", snapshot.ProductoCodigoSnapshot);
        Assert.NotNull(snapshot.CategoriaIdSnapshot);
        Assert.NotNull(snapshot.CategoriaNombreSnapshot);
        Assert.Equal(500, snapshot.CostoUnitarioCentavos);
        Assert.Equal(OrigenCostoHistorico.PRODUCTO_ACTUAL, snapshot.OrigenCosto);

        var product = await fixture.Context.Productos.SingleAsync(p => p.ProductoId == fixture.SamePriceProductId);
        product.Nombre = "Producto renombrado";
        product.PrecioCompraCentavos = 999;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        snapshot = await fixture.Context.DetallesCambioVenta.AsNoTracking().SingleAsync();
        Assert.Equal("Mismo precio", snapshot.ProductoNombreSnapshot);
        Assert.Equal(500, snapshot.CostoUnitarioCentavos);
    }

    [Fact]
    public async Task Change_CapturesReplacementSubcategorySnapshot()
    {
        await using var fixture = await CorrectionFixture.CreateAsync(RolUsuario.ADMIN);
        var replacement = await fixture.Context.Productos
            .SingleAsync(product => product.ProductoId == fixture.SamePriceProductId);
        var subcategory = new Subcategoria
        {
            CategoriaId = replacement.CategoriaId,
            Nombre = "Subcategoría de cambio",
            Activa = true,
            FechaCreacion = DateTime.UtcNow,
            FechaModificacion = DateTime.UtcNow
        };
        fixture.Context.Subcategorias.Add(subcategory);
        await fixture.Context.SaveChangesAsync();
        replacement.SubcategoriaId = subcategory.SubcategoriaId;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var sale = await fixture.CreateSaleAsync(quantity: 1);
        await fixture.ChangeService.RegisterChangeAsync(new RegisterSaleChangeRequest(
            sale.VentaId,
            sale.Lines.Single().DetalleVentaId,
            1,
            fixture.SamePriceProductId,
            CondicionProductoDevuelto.BUENO,
            true,
            "Cambio con subcategoría"));

        var snapshot = await fixture.Context.DetallesCambioVenta.AsNoTracking().SingleAsync();
        Assert.Equal(subcategory.SubcategoriaId, snapshot.SubcategoriaIdSnapshot);
        Assert.Equal("Subcategoría de cambio", snapshot.SubcategoriaNombreSnapshot);
    }

    [Fact]
    public async Task ChangeMoreExpensiveWithCash_RegistersAdditionalCashAndAffectsCashSummary()
    {
        await using var fixture = await CorrectionFixture.CreateAsync(RolUsuario.EMPLEADO);
        var sale = await fixture.CreateSaleAsync(quantity: 1);

        var result = await fixture.ChangeService.RegisterChangeAsync(new RegisterSaleChangeRequest(
            sale.VentaId,
            sale.Lines.Single().DetalleVentaId,
            1,
            fixture.ExpensiveProductId,
            CondicionProductoDevuelto.BUENO,
            true,
            "Cambio a producto mayor",
            new RegisterCorrectionPaymentRequest(MetodoPago.EFECTIVO, 500, "Diferencia cambio")));

        Assert.Equal(500, result.DiferenciaCentavos);
        Assert.NotNull(result.PagoCorreccionVentaId);

        var cash = await fixture.CashService.GetTodaySummaryAsync();
        Assert.Equal(1500, cash.TotalVentasCentavos);
        Assert.Equal(1500, cash.TotalEsperadoCentavos);
    }

    [Fact]
    public async Task ChangeMoreExpensiveWithTransfer_DoesNotAffectPhysicalCash()
    {
        await using var fixture = await CorrectionFixture.CreateAsync(RolUsuario.EMPLEADO);
        var sale = await fixture.CreateSaleAsync(quantity: 1);

        await fixture.ChangeService.RegisterChangeAsync(new RegisterSaleChangeRequest(
            sale.VentaId,
            sale.Lines.Single().DetalleVentaId,
            1,
            fixture.ExpensiveProductId,
            CondicionProductoDevuelto.BUENO,
            true,
            "Cambio transferencia",
            new RegisterCorrectionPaymentRequest(MetodoPago.TRANSFERENCIA, 500, "Diferencia cambio", "TRX-CAMBIO")));

        var payment = await fixture.Context.PagosCorreccionVenta.AsNoTracking().SingleAsync();
        Assert.Equal(MetodoPago.TRANSFERENCIA, payment.MetodoPago);
        Assert.Equal("TRX-CAMBIO", payment.Referencia);

        var cash = await fixture.CashService.GetTodaySummaryAsync();
        Assert.Equal(1000, cash.TotalEsperadoCentavos);
    }

    [Fact]
    public async Task ChangeMoreExpensiveWithBitcoin_DoesNotAffectPhysicalCash()
    {
        await using var fixture = await CorrectionFixture.CreateAsync(RolUsuario.EMPLEADO);
        var sale = await fixture.CreateSaleAsync(quantity: 1);

        await fixture.ChangeService.RegisterChangeAsync(new RegisterSaleChangeRequest(
            sale.VentaId,
            sale.Lines.Single().DetalleVentaId,
            1,
            fixture.ExpensiveProductId,
            CondicionProductoDevuelto.BUENO,
            true,
            "Cambio Bitcoin",
            new RegisterCorrectionPaymentRequest(MetodoPago.BITCOIN, 500, "Diferencia cambio", "BTC-CAMBIO")));

        var payment = await fixture.Context.PagosCorreccionVenta.AsNoTracking().SingleAsync();
        Assert.Equal(MetodoPago.BITCOIN, payment.MetodoPago);

        var cash = await fixture.CashService.GetTodaySummaryAsync();
        Assert.Equal(1000, cash.TotalEsperadoCentavos);
    }

    [Fact]
    public async Task ChangeCheaperWithAuthorizedRefund_RegistersRefund()
    {
        await using var fixture = await CorrectionFixture.CreateAsync(RolUsuario.ADMIN);
        var sale = await fixture.CreateSaleAsync(quantity: 1);

        var result = await fixture.ChangeService.RegisterChangeAsync(new RegisterSaleChangeRequest(
            sale.VentaId,
            sale.Lines.Single().DetalleVentaId,
            1,
            fixture.CheapProductId,
            CondicionProductoDevuelto.BUENO,
            true,
            "Cambio menor valor",
            Reembolso: new RegisterRefundRequest(MetodoReembolso.TRANSFERENCIA, 400, "Devolver diferencia", "REF-TRX")));

        Assert.Equal(-400, result.DiferenciaCentavos);
        Assert.NotNull(result.ReembolsoId);
        Assert.Equal(MetodoReembolso.TRANSFERENCIA, (await fixture.Context.Reembolsos.AsNoTracking().SingleAsync()).MetodoReembolso);
    }

    [Fact]
    public async Task ChangeGoodReturnedProduct_ReintegratesOriginalAndDiscountsNewProduct()
    {
        await using var fixture = await CorrectionFixture.CreateAsync(RolUsuario.EMPLEADO);
        var sale = await fixture.CreateSaleAsync(quantity: 1);
        var operationId = Guid.NewGuid();
        var newSkuId = await fixture.Context.ProductoSkus
            .Where(sku => sku.ProductoId == fixture.SamePriceProductId)
            .Select(sku => sku.ProductoSkuId)
            .SingleAsync();
        var request = new RegisterSaleChangeRequest(
            sale.VentaId,
            sale.Lines.Single().DetalleVentaId,
            1,
            fixture.SamePriceProductId,
            CondicionProductoDevuelto.BUENO,
            true,
            "Cambio bueno",
            ProductoNuevoSkuId: newSkuId,
            OperacionId: operationId);

        var first = await fixture.ChangeService.RegisterChangeAsync(request);
        var retry = await fixture.ChangeService.RegisterChangeAsync(request);

        Assert.Equal(first.DevolucionId, retry.DevolucionId);
        Assert.Equal(1, await fixture.Context.Devoluciones.CountAsync());
        Assert.Equal(1, await fixture.Context.DetallesCambioVenta.CountAsync());
        Assert.Equal(10, await fixture.GetStockAsync(fixture.OriginalProductId));
        Assert.Equal(9, await fixture.GetStockAsync(fixture.SamePriceProductId));
        Assert.Equal(newSkuId, await fixture.Context.DetallesCambioVenta
            .Select(detail => detail.ProductoSkuNuevoId).SingleAsync());
    }

    [Fact]
    public async Task ChangeDamagedProduct_RegistersWasteAndDoesNotReintegrateStock()
    {
        await using var fixture = await CorrectionFixture.CreateAsync(RolUsuario.ADMIN);
        var sale = await fixture.CreateSaleAsync(quantity: 1);

        await fixture.ChangeService.RegisterChangeAsync(new RegisterSaleChangeRequest(
            sale.VentaId,
            sale.Lines.Single().DetalleVentaId,
            1,
            fixture.SamePriceProductId,
            CondicionProductoDevuelto.DANADO,
            false,
            "Producto danado"));

        Assert.Equal(9, await fixture.GetStockAsync(fixture.OriginalProductId));
        var waste = await fixture.Context.Mermas.SingleAsync();
        Assert.NotNull(waste.ProductoSkuId);
        Assert.Equal(0, await fixture.Context.InventarioSaldos
            .Where(balance => balance.ProductoSkuId == waste.ProductoSkuId &&
                balance.Ubicacion == UbicacionInventario.BODEGA)
            .Select(balance => balance.ExistenciaFisica)
            .SingleAsync());
    }

    [Fact]
    public async Task ChangeMoreThanAvailable_IsBlocked()
    {
        await using var fixture = await CorrectionFixture.CreateAsync(RolUsuario.EMPLEADO);
        var sale = await fixture.CreateSaleAsync(quantity: 1);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.ChangeService.RegisterChangeAsync(new RegisterSaleChangeRequest(
                sale.VentaId,
                sale.Lines.Single().DetalleVentaId,
                2,
                fixture.SamePriceProductId,
                CondicionProductoDevuelto.BUENO,
                true,
                "Cantidad no disponible")));

        Assert.Contains("disponible", ex.Message);
        Assert.Equal(0, await fixture.Context.Devoluciones.CountAsync());
    }

    [Fact]
    public async Task CancelFullSale_ReintegratesStockAndMarksSaleCancelled()
    {
        await using var fixture = await CorrectionFixture.CreateAsync(RolUsuario.ADMIN);
        var sale = await fixture.CreateSaleAsync(quantity: 2);

        var request = new RegisterSaleCancellationRequest(
            sale.VentaId,
            "Anulacion completa",
            [new RegisterSaleCancellationLineRequest(sale.Lines.Single().DetalleVentaId, 2, CondicionProductoDevuelto.BUENO, true)],
            OperacionId: Guid.NewGuid());

        var first = await fixture.CancellationService.CancelSaleAsync(request);
        var retry = await fixture.CancellationService.CancelSaleAsync(request);

        Assert.Equal(first.DevolucionId, retry.DevolucionId);
        Assert.Equal(1, await fixture.Context.Devoluciones.CountAsync());
        Assert.Equal(10, await fixture.GetStockAsync(fixture.OriginalProductId));
        var storedSale = await fixture.Context.Ventas.AsNoTracking().SingleAsync(v => v.VentaId == sale.VentaId);
        Assert.Equal(EstadoVenta.ANULADA, storedSale.Estado);
    }

    [Fact]
    public async Task CancelMistakenSale_ExcludesOriginalPaymentAndRestoresExhibitionStock()
    {
        await using var fixture = await CorrectionFixture.CreateAsync(RolUsuario.ADMIN);
        var sale = await fixture.CreateSaleAsync(quantity: 1);
        var line = Assert.Single(sale.Lines);

        var before = await fixture.CashService.GetTodaySummaryAsync();
        Assert.Equal(1000, before.TotalVentasCentavos);

        await fixture.CancellationService.CancelSaleAsync(new RegisterSaleCancellationRequest(
            sale.VentaId,
            "Venta registrada por error; no hubo operacion real",
            [new RegisterSaleCancellationLineRequest(
                line.DetalleVentaId,
                1,
                CondicionProductoDevuelto.BUENO,
                ReintegraStock: true)],
            OperacionId: Guid.NewGuid(),
            EsErrorRegistroSinOperacionReal: true));

        var after = await fixture.CashService.GetTodaySummaryAsync();
        Assert.Equal(0, after.TotalVentasCentavos);
        Assert.Equal(0, after.TotalEsperadoCentavos);
        Assert.Equal(1, await fixture.Context.PagosVenta.CountAsync());

        var correction = await fixture.Context.Devoluciones.AsNoTracking().SingleAsync();
        Assert.True(correction.EsErrorRegistroSinOperacionReal);
        Assert.Equal(0, await fixture.Context.Reembolsos.CountAsync());
        Assert.Equal(10, await fixture.Context.InventarioSaldos
            .Where(balance => balance.ProductoSku.ProductoId == fixture.OriginalProductId &&
                balance.Ubicacion == UbicacionInventario.EXHIBICION)
            .Select(balance => balance.ExistenciaFisica)
            .SingleAsync());

        var movements = await new CashMovementQueryService(fixture.Context, fixture.PermissionService)
            .SearchAsync(new CashMovementFilter(DateTime.Today, DateTime.Today));
        Assert.DoesNotContain(movements.Movements, movement => movement.Documento == sale.NumeroVenta);
    }

    [Fact]
    public async Task CancelMistakenSale_WithRefundIsBlockedWithoutChangingSale()
    {
        await using var fixture = await CorrectionFixture.CreateAsync(RolUsuario.ADMIN);
        var sale = await fixture.CreateSaleAsync(quantity: 1);
        var line = Assert.Single(sale.Lines);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.CancellationService.CancelSaleAsync(new RegisterSaleCancellationRequest(
                sale.VentaId,
                "No hubo operacion real",
                [new RegisterSaleCancellationLineRequest(
                    line.DetalleVentaId,
                    1,
                    CondicionProductoDevuelto.BUENO,
                    ReintegraStock: true)],
                new RegisterRefundRequest(MetodoReembolso.EFECTIVO, 1000, "No debe existir"),
                OperacionId: Guid.NewGuid(),
                EsErrorRegistroSinOperacionReal: true)));

        Assert.Contains("no puede registrar un reembolso", ex.Message);
        Assert.Equal(0, await fixture.Context.Devoluciones.CountAsync());
        Assert.Equal(EstadoVenta.COMPLETADA,
            (await fixture.Context.Ventas.AsNoTracking().SingleAsync(item => item.VentaId == sale.VentaId)).Estado);
    }

    [Fact]
    public async Task CancelMistakenSale_RequiresRestockingEveryItemToExhibition()
    {
        await using var fixture = await CorrectionFixture.CreateAsync(RolUsuario.ADMIN);
        var sale = await fixture.CreateSaleAsync(quantity: 1);
        var line = Assert.Single(sale.Lines);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.CancellationService.CancelSaleAsync(new RegisterSaleCancellationRequest(
                sale.VentaId,
                "No hubo operacion real",
                [new RegisterSaleCancellationLineRequest(
                    line.DetalleVentaId,
                    1,
                    CondicionProductoDevuelto.FALTANTE,
                    ReintegraStock: false)],
                OperacionId: Guid.NewGuid(),
                EsErrorRegistroSinOperacionReal: true)));

        Assert.Contains("todos los productos deben volver", ex.Message);
        Assert.Equal(0, await fixture.Context.Devoluciones.CountAsync());
    }

    [Fact]
    public async Task ReturnGoodProduct_ReintegratesSoldSkuIntoWarehouseAndRetryDoesNotDuplicate()
    {
        await using var fixture = await CorrectionFixture.CreateAsync(RolUsuario.EMPLEADO);
        var sale = await fixture.CreateSaleAsync(quantity: 1);
        var saleLine = Assert.Single(sale.Lines);
        var soldSkuId = Assert.IsType<int>(saleLine.ProductoSkuId);
        var request = new RegisterReturnRequest(
            sale.VentaId,
            "Devolucion vendible",
            [new RegisterReturnLineRequest(saleLine.DetalleVentaId, 1, CondicionProductoDevuelto.BUENO, true)],
            OperacionId: Guid.NewGuid());

        var first = await fixture.ReturnService.RegisterReturnAsync(request);
        var retry = await fixture.ReturnService.RegisterReturnAsync(request);

        Assert.Equal(first.DevolucionId, retry.DevolucionId);
        var detail = await fixture.Context.DetalleDevoluciones.SingleAsync();
        Assert.Equal(soldSkuId, detail.ProductoSkuId);
        Assert.Equal(UbicacionInventario.BODEGA, detail.UbicacionReintegro);
        Assert.Equal(1, await fixture.Context.InventarioSaldos
            .Where(balance => balance.ProductoSkuId == soldSkuId &&
                balance.Ubicacion == UbicacionInventario.BODEGA)
            .Select(balance => balance.ExistenciaFisica)
            .SingleAsync());
        Assert.Equal(1, await fixture.Context.Devoluciones.CountAsync());
    }

    [Fact]
    public async Task HistoricalSaleWithoutSku_RemainsCompatibleWithoutFabricatingSnapshot()
    {
        await using var fixture = await CorrectionFixture.CreateAsync(RolUsuario.EMPLEADO);
        var sale = await fixture.CreateSaleAsync(quantity: 1);
        var line = await fixture.Context.DetalleVentas.SingleAsync();
        line.ProductoSkuId = null;
        line.CodigoSkuSnapshot = null;
        line.NombreVarianteSnapshot = null;
        await fixture.Context.SaveChangesAsync();

        await fixture.ReturnService.RegisterReturnAsync(new RegisterReturnRequest(
            sale.VentaId,
            "Venta historica",
            [new RegisterReturnLineRequest(line.DetalleVentaId, 1, CondicionProductoDevuelto.BUENO, true)],
            OperacionId: Guid.NewGuid()));

        var detail = await fixture.Context.DetalleDevoluciones.SingleAsync();
        Assert.Null(detail.ProductoSkuId);
        Assert.Null(detail.CodigoSkuSnapshot);
        Assert.Null(detail.NombreVarianteSnapshot);
        Assert.Equal(UbicacionInventario.BODEGA, detail.UbicacionReintegro);
        Assert.Equal(10, await fixture.GetStockAsync(fixture.OriginalProductId));
    }

    [Fact]
    public async Task CancelRemainderAfterPartialReturn_ProcessesOnlyRemainingQuantity()
    {
        await using var fixture = await CorrectionFixture.CreateAsync(RolUsuario.ADMIN);
        var sale = await fixture.CreateSaleAsync(quantity: 3);
        var lineId = sale.Lines.Single().DetalleVentaId;

        await fixture.ReturnService.RegisterReturnAsync(new RegisterReturnRequest(
            sale.VentaId,
            "Devolucion parcial",
            [new RegisterReturnLineRequest(lineId, 1, CondicionProductoDevuelto.BUENO, true)]));

        await fixture.CancellationService.CancelSaleAsync(new RegisterSaleCancellationRequest(
            sale.VentaId,
            "Anular remanente",
            [new RegisterSaleCancellationLineRequest(lineId, 2, CondicionProductoDevuelto.BUENO, true)]));

        Assert.Equal(3, await fixture.Context.DetalleDevoluciones.SumAsync(d => d.Cantidad));
        Assert.Equal(EstadoVenta.ANULADA, (await fixture.Context.Ventas.AsNoTracking().SingleAsync(v => v.VentaId == sale.VentaId)).Estado);
    }

    [Fact]
    public async Task CancelPartialRemainder_IsBlocked()
    {
        await using var fixture = await CorrectionFixture.CreateAsync(RolUsuario.ADMIN);
        var sale = await fixture.CreateSaleAsync(quantity: 2);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.CancellationService.CancelSaleAsync(new RegisterSaleCancellationRequest(
                sale.VentaId,
                "Anulacion parcial invalida",
                [new RegisterSaleCancellationLineRequest(sale.Lines.Single().DetalleVentaId, 1, CondicionProductoDevuelto.BUENO, true)])));

        Assert.Contains("remanente", ex.Message);
    }

    [Fact]
    public async Task CancelTwice_IsBlocked()
    {
        await using var fixture = await CorrectionFixture.CreateAsync(RolUsuario.ADMIN);
        var sale = await fixture.CreateSaleAsync(quantity: 1);
        var lineId = sale.Lines.Single().DetalleVentaId;

        await fixture.CancellationService.CancelSaleAsync(new RegisterSaleCancellationRequest(
            sale.VentaId,
            "Anulacion",
            [new RegisterSaleCancellationLineRequest(lineId, 1, CondicionProductoDevuelto.BUENO, true)]));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.CancellationService.CancelSaleAsync(new RegisterSaleCancellationRequest(
                sale.VentaId,
                "Anulacion repetida",
                [new RegisterSaleCancellationLineRequest(lineId, 1, CondicionProductoDevuelto.BUENO, true)])));
    }

    [Fact]
    public async Task ChangeFailureDuringAudit_RollsBackCompletely()
    {
        await using var fixture = await CorrectionFixture.CreateAsync(RolUsuario.EMPLEADO);
        var sale = await fixture.CreateSaleAsync(quantity: 1);

        var failingService = new SaleChangeService(
            fixture.Context,
            fixture.CurrentSession,
            new ThrowingAuditService(),
            fixture.PermissionService);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            failingService.RegisterChangeAsync(new RegisterSaleChangeRequest(
                sale.VentaId,
                sale.Lines.Single().DetalleVentaId,
                1,
                fixture.SamePriceProductId,
                CondicionProductoDevuelto.BUENO,
                true,
                "Fallo auditoria")));

        fixture.Context.ChangeTracker.Clear();
        Assert.Equal(0, await fixture.Context.Devoluciones.CountAsync());
        Assert.Equal(0, await fixture.Context.DetallesCambioVenta.CountAsync());
        Assert.Equal(9, await fixture.GetStockAsync(fixture.OriginalProductId));
        Assert.Equal(10, await fixture.GetStockAsync(fixture.SamePriceProductId));
    }

    [Fact]
    public async Task CancelWithCashRefund_DoesNotAdjustCashTwice()
    {
        await using var fixture = await CorrectionFixture.CreateAsync(RolUsuario.ADMIN);
        var sale = await fixture.CreateSaleAsync(quantity: 1);

        await fixture.CancellationService.CancelSaleAsync(new RegisterSaleCancellationRequest(
            sale.VentaId,
            "Anulacion con reembolso",
            [new RegisterSaleCancellationLineRequest(sale.Lines.Single().DetalleVentaId, 1, CondicionProductoDevuelto.BUENO, true)],
            new RegisterRefundRequest(MetodoReembolso.EFECTIVO, 1000, "Reembolso anulacion")));

        var summary = await fixture.CashService.GetTodaySummaryAsync();
        Assert.Equal(1000, summary.TotalVentasCentavos);
        Assert.Equal(1000, summary.TotalReembolsosCentavos);
        Assert.Equal(0, summary.TotalEsperadoCentavos);
    }

    [Fact]
    public async Task CancellationKeepsOriginalSaleDetailsAndPayments()
    {
        await using var fixture = await CorrectionFixture.CreateAsync(RolUsuario.ADMIN);
        var sale = await fixture.CreateSaleAsync(quantity: 1);
        var lineId = sale.Lines.Single().DetalleVentaId;

        await fixture.CancellationService.CancelSaleAsync(new RegisterSaleCancellationRequest(
            sale.VentaId,
            "Anulacion conserva venta",
            [new RegisterSaleCancellationLineRequest(lineId, 1, CondicionProductoDevuelto.BUENO, true)]));

        var storedSale = await fixture.Context.Ventas
            .AsNoTracking()
            .Include(v => v.Detalles)
            .Include(v => v.Pagos)
            .SingleAsync(v => v.VentaId == sale.VentaId);

        Assert.Equal(EstadoVenta.ANULADA, storedSale.Estado);
        Assert.Single(storedSale.Detalles);
        Assert.Single(storedSale.Pagos);
        Assert.Equal(1000, storedSale.TotalCentavos);
    }

    [Fact]
    public async Task ExistingReturnRegression_CashAndTransferRefundsAndStockRules()
    {
        await using var fixture = await CorrectionFixture.CreateAsync(RolUsuario.ADMIN);
        var sale = await fixture.CreateSaleAsync(quantity: 2);
        var lineId = sale.Lines.Single().DetalleVentaId;

        await fixture.ReturnService.RegisterReturnAsync(new RegisterReturnRequest(
            sale.VentaId,
            "Devolucion buena",
            [new RegisterReturnLineRequest(lineId, 1, CondicionProductoDevuelto.BUENO, true)],
            new RegisterRefundRequest(MetodoReembolso.EFECTIVO, 1000, "Reembolso efectivo")));

        await fixture.ReturnService.RegisterReturnAsync(new RegisterReturnRequest(
            sale.VentaId,
            "Devolucion danada",
            [new RegisterReturnLineRequest(lineId, 1, CondicionProductoDevuelto.DANADO, false)],
            new RegisterRefundRequest(MetodoReembolso.TRANSFERENCIA, 1000, "Reembolso transferencia", "REF-002")));

        Assert.Equal(9, await fixture.GetStockAsync(fixture.OriginalProductId));
        Assert.Equal(1, await fixture.Context.Mermas.CountAsync());
        Assert.Equal(2, await fixture.Context.Reembolsos.CountAsync());
    }

    private sealed class CorrectionFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private CorrectionFixture(
            SqliteConnection connection,
            AppDbContext context,
            TestCurrentSession currentSession,
            SaleService saleService,
            ReturnService returnService,
            SaleChangeService changeService,
            SaleCancellationService cancellationService,
            CashService cashService,
            EmployeePermissionService permissionService,
            int originalProductId,
            int samePriceProductId,
            int expensiveProductId,
            int cheapProductId)
        {
            _connection = connection;
            Context = context;
            CurrentSession = currentSession;
            SaleService = saleService;
            ReturnService = returnService;
            ChangeService = changeService;
            CancellationService = cancellationService;
            CashService = cashService;
            PermissionService = permissionService;
            OriginalProductId = originalProductId;
            SamePriceProductId = samePriceProductId;
            ExpensiveProductId = expensiveProductId;
            CheapProductId = cheapProductId;
        }

        public AppDbContext Context { get; }
        public TestCurrentSession CurrentSession { get; }
        public SaleService SaleService { get; }
        public ReturnService ReturnService { get; }
        public SaleChangeService ChangeService { get; }
        public SaleCancellationService CancellationService { get; }
        public CashService CashService { get; }
        public EmployeePermissionService PermissionService { get; }
        public int OriginalProductId { get; }
        public int SamePriceProductId { get; }
        public int ExpensiveProductId { get; }
        public int CheapProductId { get; }

        public static async Task<CorrectionFixture> CreateAsync(RolUsuario role)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;

            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var user = new Usuario
            {
                NombreCompleto = "Usuario pruebas",
                NombreUsuario = $"user-{Guid.NewGuid():N}",
                PasswordHash = "hash",
                Rol = role,
                Activo = true,
                FechaCreacion = DateTime.UtcNow
            };

            var category = new Categoria
            {
                Nombre = $"Categoria-{Guid.NewGuid():N}",
                Activa = true,
                FechaCreacion = DateTime.UtcNow
            };

            context.Usuarios.Add(user);
            context.Categorias.Add(category);
            await context.SaveChangesAsync();

            var original = CreateProduct(category.CategoriaId, "Original", 1000);
            var same = CreateProduct(category.CategoriaId, "Mismo precio", 1000);
            var expensive = CreateProduct(category.CategoriaId, "Mas caro", 1500);
            var cheap = CreateProduct(category.CategoriaId, "Mas barato", 600);

            context.Productos.AddRange(original, same, expensive, cheap);
            context.Cajas.Add(new Caja
            {
                FechaOperacion = DateOnly.FromDateTime(DateTime.Now),
                FechaApertura = DateTime.UtcNow,
                MontoInicialCentavos = 0,
                MontoCierreCentavos = 0,
                Estado = EstadoCaja.ABIERTA,
                UsuarioAperturaId = user.UsuarioId
            });
            await context.SaveChangesAsync();

            var session = new TestCurrentSession(new UsuarioSesion(
                user.UsuarioId,
                user.NombreCompleto,
                user.NombreUsuario,
                user.Rol));

            var audit = new AuditService(context);
            var permissionService = new EmployeePermissionService(context, session, audit);
            if (user.Rol == RolUsuario.EMPLEADO)
            {
                await permissionService.EnsureRecommendedPermissionsForEmployeeAsync(user.UsuarioId);
            }

            var creditService = new CreditService(context, session, permissionService, audit);
            var saleService = new SaleService(context, session, audit, permissionService, creditService);
            var returnService = new ReturnService(context, session, audit, permissionService);
            var changeService = new SaleChangeService(context, session, audit, permissionService);
            var cancellationService = new SaleCancellationService(context, session, audit);
            var cashService = new CashService(context, session, audit, new NullNotificationService(), permissionService);

            return new CorrectionFixture(
                connection,
                context,
                session,
                saleService,
                returnService,
                changeService,
                cancellationService,
                cashService,
                permissionService,
                original.ProductoId,
                same.ProductoId,
                expensive.ProductoId,
                cheap.ProductoId);
        }

        public Task<SaleResult> CreateSaleAsync(int quantity)
        {
            return SaleService.RegisterSaleAsync(new RegisterSaleRequest(
                [new SaleLineRequest(OriginalProductId, quantity)],
                [new SalePaymentRequest(MetodoPago.EFECTIVO, 1000 * quantity)]));
        }

        public async Task<int> GetStockAsync(int productId)
        {
            return await Context.InventarioSaldos
                .AsNoTracking()
                .Where(balance => balance.ProductoSku.ProductoId == productId)
                .SumAsync(balance => balance.ExistenciaFisica);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private static Producto CreateProduct(int categoryId, string name, long price)
        {
            var productCode = $"{name.ToUpperInvariant().Replace(" ", "-")}-{Guid.NewGuid():N}";
            return new Producto
            {
                CodigoInterno = productCode,
                Nombre = name,
                CategoriaId = categoryId,
                PrecioCompraCentavos = price / 2,
                PrecioVentaCentavos = price,
                StockActual = 10,
                StockMinimo = 1,
                Estado = EstadoProducto.ACTIVO,
                FechaCreacion = DateTime.UtcNow,
                Skus =
                {
                    new ProductoSku
                    {
                        CodigoSku = productCode,
                        Activo = true,
                        EsPrincipal = true,
                        SaldosInventario =
                        {
                            new InventarioSaldo
                            {
                                Ubicacion = UbicacionInventario.EXHIBICION,
                                ExistenciaFisica = 10,
                                Version = 1
                            },
                            new InventarioSaldo { Ubicacion = UbicacionInventario.BODEGA, Version = 1 },
                            new InventarioSaldo { Ubicacion = UbicacionInventario.SIN_ASIGNAR, Version = 1 }
                        }
                    }
                }
            };
        }
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
        public bool TieneRol(RolUsuario rol) => UsuarioActual?.Rol == rol;
        public void IniciarSesion(UsuarioSesion usuario) => UsuarioActual = usuario;
        public void CerrarSesion() => UsuarioActual = null;
    }

    private sealed class ThrowingAuditService : IAuditService
    {
        public Task RegistrarAsync(AuditEntryRequest request, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Fallo simulado al guardar auditoria.");
        }
    }
}
