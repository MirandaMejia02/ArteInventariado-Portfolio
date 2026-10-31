using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public class DashboardQueryServiceTests
{
    [Fact]
    public async Task Overview_UsesNetSalesComparisonAndRealCounts()
    {
        await using var fixture = await DashboardFixture.CreateAsync();

        var overview = await fixture.Service.GetOverviewAsync();

        Assert.Equal(8_300, overview.SalesToday.NetSalesCentavos);
        Assert.Equal(1, overview.SalesToday.SaleCount);
        Assert.Equal(5_000, overview.SalesToday.Comparison.PreviousValue);
        Assert.Equal(66.0m, overview.SalesToday.Comparison.Percentage);
        Assert.Equal(2, overview.ActiveProducts);
        Assert.Equal(1, overview.RegisteredCustomers);
        Assert.Equal(3, overview.PendingOrders);
        Assert.True(overview.CashExpected.HasOpenCash);
        Assert.Equal(12_345, overview.CashExpected.ExpectedAmountCentavos);
    }

    [Fact]
    public async Task Overview_RepresentsMissingCashWithoutFalseZero()
    {
        await using var fixture = await DashboardFixture.CreateAsync(hasOpenCash: false);

        var overview = await fixture.Service.GetOverviewAsync();

        Assert.False(overview.CashExpected.HasOpenCash);
        Assert.Null(overview.CashExpected.ExpectedAmountCentavos);
    }

    [Fact]
    public async Task SalesTrend_FillsDaysWithoutMovementWithZero()
    {
        await using var fixture = await DashboardFixture.CreateAsync();

        var trend = await fixture.Service.GetSalesTrendAsync(DashboardSalesRange.LastSevenDays);

        Assert.Equal(7, trend.Points.Count);
        Assert.Equal(8_300, trend.Points.Single(p => p.Date == new DateOnly(2026, 8, 8)).NetSalesCentavos);
        Assert.Equal(5_000, trend.Points.Single(p => p.Date == new DateOnly(2026, 8, 7)).NetSalesCentavos);
        Assert.Equal(0, trend.Points.Single(p => p.Date == new DateOnly(2026, 8, 6)).NetSalesCentavos);
    }

    [Fact]
    public async Task CategorySales_AllocatesGeneralDiscountAndCorrectionsExactly()
    {
        await using var fixture = await DashboardFixture.CreateAsync();

        var result = await fixture.Service.GetCategorySalesAsync(DashboardSalesRange.LastSevenDays);

        Assert.Equal(13_300, result.PositiveNetSalesCentavos);
        Assert.Equal(2, result.Categories.Count);
        Assert.Equal(7_700, result.Categories.Single(c => c.CategoryName == "Madera").NetSalesCentavos);
        Assert.Equal(5_600, result.Categories.Single(c => c.CategoryName == "Textil").NetSalesCentavos);
        Assert.Equal(100.0m, result.Categories.Sum(c => c.Percentage));
    }

    [Fact]
    public async Task TopProducts_UsesSoldMinusReturnedUnits()
    {
        await using var fixture = await DashboardFixture.CreateAsync();

        var products = await fixture.Service.GetTopProductsAsync(DashboardSalesRange.LastSevenDays);

        Assert.Equal(2, products.Count);
        Assert.Equal(2, products.Single(p => p.Code == "A-1").NetUnitsSold);
        Assert.Equal(1, products.Single(p => p.Code == "B-1").NetUnitsSold);
    }

    [Fact]
    public async Task RecentActivity_TranslatesAndSuppressesExceptionText()
    {
        await using var fixture = await DashboardFixture.CreateAsync();

        var activity = await fixture.Service.GetRecentActivityAsync(2);

        Assert.Equal("Venta registrada", activity[0].Title);
        Assert.Equal("Venta registrada", activity[0].Description);
        Assert.DoesNotContain("Exception", activity[0].Description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Compra registrada", activity[1].Title);
    }

    [Fact]
    public async Task Queries_RejectEmployeeAndInactiveAdmin()
    {
        await using var employeeFixture = await DashboardFixture.CreateAsync(RolUsuario.EMPLEADO);
        await Assert.ThrowsAsync<InvalidOperationException>(() => employeeFixture.Service.GetOverviewAsync());

        await using var inactiveFixture = await DashboardFixture.CreateAsync(activeUser: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => inactiveFixture.Service.GetOverviewAsync());
    }

    [Fact]
    public async Task Queries_HonorCancellationAndDoNotWrite()
    {
        await using var fixture = await DashboardFixture.CreateAsync();
        var before = await fixture.Context.Bitacora.CountAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Service.GetSalesTrendAsync(DashboardSalesRange.LastSevenDays, cancellation.Token));
        Assert.Equal(before, await fixture.Context.Bitacora.CountAsync());
    }

    private sealed class DashboardFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private DashboardFixture(
            SqliteConnection connection,
            AppDbContext context,
            DashboardQueryService service)
        {
            _connection = connection;
            Context = context;
            Service = service;
        }

        public AppDbContext Context { get; }
        public DashboardQueryService Service { get; }

        public static async Task<DashboardFixture> CreateAsync(
            RolUsuario role = RolUsuario.ADMIN,
            bool activeUser = true,
            bool hasOpenCash = true)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var nowUtc = new DateTimeOffset(2026, 8, 8, 21, 30, 0, TimeSpan.Zero);
            var user = new Usuario
            {
                NombreCompleto = "Administrador dashboard",
                NombreUsuario = "dashboard-admin",
                PasswordHash = "hash",
                Rol = role,
                Activo = activeUser,
                FechaCreacion = nowUtc.UtcDateTime.AddDays(-10)
            };
            context.Usuarios.Add(user);
            await context.SaveChangesAsync();

            var categoryA = new Categoria { Nombre = "Madera", Activa = true };
            var categoryB = new Categoria { Nombre = "Textil", Activa = true };
            context.Categorias.AddRange(categoryA, categoryB);
            await context.SaveChangesAsync();

            var productA = new Producto
            {
                CodigoInterno = "A-1",
                Nombre = "Producto A",
                CategoriaId = categoryA.CategoriaId,
                PrecioCompraCentavos = 1_000,
                PrecioVentaCentavos = 3_000,
                StockActual = 8,
                StockMinimo = 2,
                Estado = EstadoProducto.ACTIVO
            };
            var productB = new Producto
            {
                CodigoInterno = "B-1",
                Nombre = "Producto B",
                CategoriaId = categoryB.CategoriaId,
                PrecioCompraCentavos = 1_500,
                PrecioVentaCentavos = 4_000,
                StockActual = 5,
                StockMinimo = 2,
                Estado = EstadoProducto.ACTIVO
            };
            context.Productos.AddRange(productA, productB);
            context.Productos.Add(new Producto
            {
                CodigoInterno = "I-1",
                Nombre = "Inactivo",
                CategoriaId = categoryA.CategoriaId,
                Estado = EstadoProducto.INACTIVO
            });

            var customer = new Cliente
            {
                CodigoCliente = "CLI-0001",
                NombreCompleto = "Cliente dashboard",
                NombreNormalizado = "cliente dashboard",
                Estado = EstadoCliente.ACTIVO,
                UsuarioCreacionId = user.UsuarioId,
                FechaCreacion = nowUtc.UtcDateTime,
                FechaActualizacion = nowUtc.UtcDateTime
            };
            context.Clientes.Add(customer);
            await context.SaveChangesAsync();

            var cash = new Caja
            {
                FechaOperacion = new DateOnly(2026, 8, 8),
                FechaApertura = nowUtc.UtcDateTime.AddHours(-4),
                MontoInicialCentavos = 1_000,
                Estado = hasOpenCash ? EstadoCaja.ABIERTA : EstadoCaja.CERRADA,
                UsuarioAperturaId = user.UsuarioId
            };
            context.Cajas.Add(cash);
            await context.SaveChangesAsync();

            var todaySale = new Venta
            {
                NumeroVenta = "VTA-TODAY",
                CajaId = cash.CajaId,
                UsuarioId = user.UsuarioId,
                FechaVenta = nowUtc.UtcDateTime.AddHours(-1),
                SubtotalCentavos = 10_000,
                DescuentoCentavos = 1_000,
                TotalCentavos = 9_000,
                PagadoCentavos = 9_000,
                Estado = EstadoVenta.COMPLETADA
            };
            var yesterdaySale = new Venta
            {
                NumeroVenta = "VTA-YESTERDAY",
                CajaId = cash.CajaId,
                UsuarioId = user.UsuarioId,
                FechaVenta = nowUtc.UtcDateTime.AddDays(-1).AddHours(-1),
                SubtotalCentavos = 5_000,
                TotalCentavos = 5_000,
                PagadoCentavos = 5_000,
                Estado = EstadoVenta.COMPLETADA
            };
            context.Ventas.AddRange(todaySale, yesterdaySale);
            await context.SaveChangesAsync();

            var todayLineA = new DetalleVenta
            {
                VentaId = todaySale.VentaId,
                ProductoId = productA.ProductoId,
                Cantidad = 2,
                PrecioUnitarioCentavos = 3_000,
                SubtotalCentavos = 6_000,
                TotalCentavos = 6_000
            };
            context.DetalleVentas.AddRange(
                todayLineA,
                new DetalleVenta
                {
                    VentaId = todaySale.VentaId,
                    ProductoId = productB.ProductoId,
                    Cantidad = 1,
                    PrecioUnitarioCentavos = 4_000,
                    SubtotalCentavos = 4_000,
                    TotalCentavos = 4_000
                },
                new DetalleVenta
                {
                    VentaId = yesterdaySale.VentaId,
                    ProductoId = productA.ProductoId,
                    Cantidad = 1,
                    PrecioUnitarioCentavos = 5_000,
                    SubtotalCentavos = 5_000,
                    TotalCentavos = 5_000
                });
            await context.SaveChangesAsync();

            var correction = new Devolucion
            {
                NumeroDevolucion = "DEV-1",
                VentaId = todaySale.VentaId,
                UsuarioId = user.UsuarioId,
                CajaId = cash.CajaId,
                FechaDevolucion = nowUtc.UtcDateTime.AddMinutes(-30),
                TipoCorreccion = TipoCorreccionVenta.CAMBIO,
                TotalDevueltoCentavos = 2_700,
                Motivo = "Cambio de prueba"
            };
            context.Devoluciones.Add(correction);
            await context.SaveChangesAsync();
            context.DetalleDevoluciones.Add(new DetalleDevolucion
            {
                DevolucionId = correction.DevolucionId,
                DetalleVentaId = todayLineA.DetalleVentaId,
                ProductoId = productA.ProductoId,
                Cantidad = 1,
                PrecioUnitarioCentavos = 3_000,
                DescuentoProporcionalCentavos = 300,
                SubtotalDevueltoCentavos = 2_700,
                CondicionProducto = CondicionProductoDevuelto.BUENO,
                ReintegraStock = true
            });
            var changeMovement = new MovimientoInventario
            {
                ProductoId = productB.ProductoId,
                VentaId = todaySale.VentaId,
                UsuarioId = user.UsuarioId,
                TipoMovimiento = TipoMovimientoInventario.SALIDA,
                Cantidad = -1,
                StockAnterior = 6,
                StockNuevo = 5,
                Motivo = "Producto sustituto de cambio de prueba",
                FechaMovimiento = nowUtc.UtcDateTime.AddMinutes(-30)
            };
            context.MovimientosInventario.Add(changeMovement);
            await context.SaveChangesAsync();
            context.DetallesCambioVenta.Add(new DetalleCambioVenta
            {
                DevolucionId = correction.DevolucionId,
                DetalleVentaOrigenId = todayLineA.DetalleVentaId,
                ProductoNuevoId = productB.ProductoId,
                Cantidad = 1,
                PrecioUnitarioCentavos = 2_000,
                SubtotalCentavos = 2_000,
                TotalCentavos = 2_000,
                MovimientoInventarioId = changeMovement.MovimientoInventarioId
            });

            foreach (var state in new[]
                     {
                         EstadoPedidoPersonalizado.CONFIRMADO,
                         EstadoPedidoPersonalizado.EN_PRODUCCION,
                         EstadoPedidoPersonalizado.LISTO_PARA_ENTREGA,
                         EstadoPedidoPersonalizado.ENTREGADO
                     })
            {
                context.PedidosPersonalizados.Add(new PedidoPersonalizado
                {
                    CodigoPedido = $"PED-{(int)state}",
                    NombreDestinatarioSnapshot = "Cliente",
                    Estado = state,
                    FechaPedido = nowUtc.UtcDateTime,
                    FechaPrometida = nowUtc.UtcDateTime.AddDays(2),
                    UsuarioCreadorId = user.UsuarioId,
                    UsuarioUltimaActualizacionId = user.UsuarioId,
                    FechaCreacion = nowUtc.UtcDateTime,
                    FechaActualizacion = nowUtc.UtcDateTime
                });
            }

            context.Bitacora.AddRange(
                new Bitacora
                {
                    UsuarioId = user.UsuarioId,
                    TipoAccion = TipoBitacora.COMPRA_CREADA,
                    Entidad = "compras",
                    Descripcion = "Compra CMP-1 registrada.",
                    FechaAccion = nowUtc.UtcDateTime.AddMinutes(-10)
                },
                new Bitacora
                {
                    UsuarioId = user.UsuarioId,
                    TipoAccion = TipoBitacora.VENTA_REALIZADA,
                    Entidad = "ventas",
                    Descripcion = "System.Exception: dato interno",
                    FechaAccion = nowUtc.UtcDateTime.AddMinutes(-5)
                });
            await context.SaveChangesAsync();

            var session = new TestCurrentSession(new UsuarioSesion(
                user.UsuarioId,
                user.NombreCompleto,
                user.NombreUsuario,
                user.Rol));
            var cashService = new FakeCashService(hasOpenCash);
            var timeProvider = new FixedTimeProvider(nowUtc);
            var service = new DashboardQueryService(context, session, cashService, timeProvider);
            return new DashboardFixture(connection, context, service);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class TestCurrentSession(UsuarioSesion user) : ICurrentSession
    {
        public UsuarioSesion? UsuarioActual { get; private set; } = user;
        public bool EstaAutenticado => UsuarioActual is not null;
        public bool EsAdmin => TieneRol(RolUsuario.ADMIN);
        public bool TieneRol(RolUsuario rol) => UsuarioActual?.Rol == rol;
        public void IniciarSesion(UsuarioSesion usuario) => UsuarioActual = usuario;
        public void CerrarSesion() => UsuarioActual = null;
    }

    private sealed class FakeCashService(bool hasOpenCash) : ICashService
    {
        public Task<CashSummary> GetTodaySummaryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CashSummary(
                hasOpenCash ? 1 : null,
                new DateOnly(2026, 8, 8),
                hasOpenCash ? EstadoCaja.ABIERTA : null,
                0,
                0,
                0,
                0,
                0,
                hasOpenCash ? 12_345 : 0,
                null,
                null,
                null,
                null,
                null));

        public Task<CashExpectedBalanceSummary> GetTodayExpectedCashBalanceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CashExpectedBalanceSummary(
                new DateOnly(2026, 8, 8), hasOpenCash ? 1 : 0, hasOpenCash ? 12_345 : null, false));

        public Task<CashOpeningReference?> GetPreviousDayCashOpeningReferenceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<CashOpeningReference?>(null);

        public Task<IReadOnlyList<CashSummary>> GetSessionHistoryAsync(
            DateOnly desde,
            DateOnly hasta,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CashSummary> OpenCashAsync(OpenCashRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CashRecoveryStatus?> GetOpenCashRecoveryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<CashRecoveryStatus?>(null);
        public Task<CashSummary> ContinueCashAsync(ContinueCashRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CashSummary> RegularizeCashAsync(RegularizeCashRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CashSummary> RegularizeClosedCashDayAsync(RegularizeClosedCashDayRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CashSummary> OpenCashAutomaticallyIfEnabledAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CashSummary> CloseCashAsync(CloseCashRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CashWithdrawalSummary> RegisterWithdrawalAsync(RegisterWithdrawalRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CashContributionSummary> RegisterCashContributionAsync(RegisterCashContributionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CashExpenseSummary> RegisterExpenseAsync(RegisterExpenseRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExpenseCategoryOption>> GetExpenseCategoriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExpenseCategoryOption>>([]);
        public Task<IReadOnlyList<CashWithdrawalSummary>> GetTodayWithdrawalsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CashExpenseSummary>> GetTodayExpensesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CashRefundSummary>> GetTodayRefundsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
