using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public class CommercialAnalyticsQueryServiceTests
{
    [Fact]
    public async Task Report_CalculatesQuotationOrderConversionPaymentAndRefundMetrics()
    {
        await using var fixture = await CommercialFixture.CreateAsync();

        var report = await fixture.Service.GetAsync(Year2026());

        Assert.Equal(4, report.Quotations.Created.Value);
        Assert.Equal(13_000, report.Quotations.QuotedAmount.Value);
        Assert.Equal(1, report.Quotations.Accepted);
        Assert.Equal(1, report.Quotations.Rejected);
        Assert.Equal(1, report.Quotations.Expired);
        Assert.Equal(1, report.Quotations.Pending);
        Assert.Equal(1, report.Quotations.Converted);
        Assert.Equal(7_000, report.Quotations.ConvertedValueCentavos);
        Assert.Equal(48m, report.Quotations.AverageConversionHours);

        Assert.Equal(3, report.Orders.Created.Value);
        Assert.Equal(1, report.Orders.Pending);
        Assert.Equal(1, report.Orders.Delivered);
        Assert.Equal(1, report.Orders.Cancelled);
        Assert.Equal(1, report.Orders.Delayed);
        Assert.Equal(14_000, report.Orders.TotalAmountCentavos);
        Assert.Equal(3_000, report.Orders.AdvancesCentavos);
        Assert.Equal(7_000, report.Orders.PaymentsCentavos);
        Assert.Equal(500, report.Orders.RefundsCentavos);
        Assert.Equal(5_000, report.Orders.OutstandingBalanceCentavos);
        Assert.Equal(1, report.Orders.ConvertedToSale);

        Assert.Equal(25m, Conversion(report, "Cotizaciones aceptadas").Percentage);
        Assert.Equal(25m, Conversion(report, "Cotización → pedido").Percentage);
        Assert.False(Conversion(report, "Cotización → venta directa").Available);
        Assert.Equal(33.33m, Math.Round(Conversion(report, "Pedido → venta").Percentage!.Value, 2));
        Assert.Equal(8, report.Trend.Count);
        Assert.Equal("Cliente comercial", report.TopCustomers[0].CustomerName);
        Assert.Contains("no está disponible", report.CoverageNote, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Report_IsReadOnlyAndHonorsPreCancelledRequest()
    {
        await using var fixture = await CommercialFixture.CreateAsync();
        var before = await fixture.Context.Cotizaciones.CountAsync();

        await fixture.Service.GetAsync(Year2026());

        Assert.Equal(before, await fixture.Context.Cotizaciones.CountAsync());
        Assert.Empty(fixture.Context.ChangeTracker.Entries());

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Service.GetAsync(Year2026(), cancellation.Token));
    }

    [Theory]
    [InlineData(2026, 8, 14, EstadoPedidoPersonalizado.CONFIRMADO, true)]
    [InlineData(2026, 8, 15, EstadoPedidoPersonalizado.CONFIRMADO, false)]
    [InlineData(2026, 8, 14, EstadoPedidoPersonalizado.ENTREGADO, false)]
    [InlineData(2026, 8, 14, EstadoPedidoPersonalizado.CANCELADO, false)]
    public void DelayedOrder_RequiresValidPastPromiseAndOpenState(
        int year,
        int month,
        int day,
        EstadoPedidoPersonalizado state,
        bool expected)
    {
        var delayed = CommercialAnalyticsQueryService.IsDelayed(
            new DateTime(2026, 8, 1),
            new DateTime(year, month, day),
            state == EstadoPedidoPersonalizado.ENTREGADO ? new DateTime(2026, 8, 14) : null,
            state,
            new DateTime(2026, 8, 15));

        Assert.Equal(expected, delayed);
    }

    [Fact]
    public void DelayedOrder_ExcludesDefaultOrEarlierPromisedDates()
    {
        Assert.False(CommercialAnalyticsQueryService.IsDelayed(
            new DateTime(2026, 8, 1), default, null,
            EstadoPedidoPersonalizado.CONFIRMADO, new DateTime(2026, 8, 15)));
        Assert.False(CommercialAnalyticsQueryService.IsDelayed(
            new DateTime(2026, 8, 10), new DateTime(2026, 8, 9), null,
            EstadoPedidoPersonalizado.CONFIRMADO, new DateTime(2026, 8, 15)));
    }

    private static CommercialConversionMetric Conversion(CommercialAnalyticsReport report, string name) =>
        report.Conversions.Single(metric => metric.Name == name);

    private static AnalyticsQueryRequest Year2026() => new(new AnalyticsPeriodRequest(
        AnalyticsPeriodKind.CustomRange,
        new DateOnly(2026, 1, 1),
        new DateOnly(2026, 12, 31)));

    private sealed class CommercialFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private CommercialFixture(
            SqliteConnection connection,
            AppDbContext context,
            CommercialAnalyticsQueryService service)
        {
            _connection = connection;
            Context = context;
            Service = service;
        }

        public AppDbContext Context { get; }
        public CommercialAnalyticsQueryService Service { get; }

        public static async Task<CommercialFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var admin = new Usuario
            {
                NombreCompleto = "Admin comercial",
                NombreUsuario = "admin-commercial",
                PasswordHash = "hash",
                Rol = RolUsuario.ADMIN,
                Activo = true,
                FechaCreacion = Utc(2025, 1, 1)
            };
            context.Usuarios.Add(admin);
            await context.SaveChangesAsync();

            var customer = new Cliente
            {
                CodigoCliente = "CLI-0001",
                NombreCompleto = "Cliente comercial",
                NombreNormalizado = "CLIENTE COMERCIAL",
                Estado = EstadoCliente.ACTIVO,
                UsuarioCreacionId = admin.UsuarioId,
                FechaCreacion = Utc(2025, 1, 1),
                FechaActualizacion = Utc(2025, 1, 1)
            };
            var cash = new Caja
            {
                FechaOperacion = new DateOnly(2026, 1, 15),
                FechaApertura = Utc(2026, 1, 15, 8),
                FechaCierre = Utc(2026, 1, 15, 18),
                Estado = EstadoCaja.CERRADA,
                UsuarioAperturaId = admin.UsuarioId,
                UsuarioCierreId = admin.UsuarioId
            };
            context.AddRange(customer, cash);
            context.ConfiguracionSistema.Add(new ConfiguracionSistema
            {
                Clave = DashboardQueryService.BusinessTimeZoneKey,
                Valor = DashboardQueryService.DefaultBusinessTimeZoneId,
                FechaModificacion = Utc(2025, 1, 1)
            });
            await context.SaveChangesAsync();

            var sale = new Venta
            {
                NumeroVenta = "VTA-COM-1",
                CajaId = cash.CajaId,
                UsuarioId = admin.UsuarioId,
                ClienteId = customer.ClienteId,
                ClienteNombreSnapshot = customer.NombreCompleto,
                UsuarioNombreSnapshot = admin.NombreCompleto,
                FechaVenta = Utc(2026, 1, 15),
                SubtotalCentavos = 7_000,
                TotalCentavos = 7_000,
                PagadoCentavos = 7_000,
                Estado = EstadoVenta.COMPLETADA
            };
            context.Ventas.Add(sale);
            await context.SaveChangesAsync();

            var converted = Quote("COT-1", customer, admin, Utc(2026, 1, 10, 10), 7_000, EstadoCotizacion.CONVERTIDA);
            var rejected = Quote("COT-2", customer, admin, Utc(2026, 2, 1), 2_000, EstadoCotizacion.RECHAZADA);
            var expired = Quote("COT-3", customer, admin, Utc(2026, 3, 1), 1_000, EstadoCotizacion.VENCIDA);
            var pending = Quote("COT-4", customer, admin, Utc(2026, 4, 1), 3_000, EstadoCotizacion.EMITIDA);
            context.AddRange(converted, rejected, expired, pending);
            await context.SaveChangesAsync();

            context.Set<HistorialEstadoCotizacion>().AddRange(
                QuoteHistory(converted, admin, EstadoCotizacion.ACEPTADA, Utc(2026, 1, 11, 10)),
                QuoteHistory(converted, admin, EstadoCotizacion.CONVERTIDA, Utc(2026, 1, 12, 10)),
                QuoteHistory(rejected, admin, EstadoCotizacion.RECHAZADA, Utc(2026, 2, 2)),
                QuoteHistory(expired, admin, EstadoCotizacion.VENCIDA, Utc(2026, 3, 10)));

            var delivered = Order("PED-1", customer, admin, Utc(2026, 1, 12), Utc(2026, 1, 20),
                EstadoPedidoPersonalizado.ENTREGADO, 7_000, 0);
            delivered.CotizacionId = converted.CotizacionId;
            delivered.VentaId = sale.VentaId;
            delivered.FechaEntregaReal = Utc(2026, 1, 16);
            delivered.FechaConversion = Utc(2026, 1, 16);
            var delayed = Order("PED-2", customer, admin, Utc(2026, 8, 1), Utc(2026, 8, 5),
                EstadoPedidoPersonalizado.CONFIRMADO, 5_000, 5_000);
            var cancelled = Order("PED-3", customer, admin, Utc(2026, 7, 1), Utc(2026, 7, 5),
                EstadoPedidoPersonalizado.CANCELADO, 2_000, 0);
            cancelled.FechaCancelacion = Utc(2026, 7, 2);
            context.AddRange(delivered, delayed, cancelled);
            await context.SaveChangesAsync();

            context.Set<HistorialEstadoPedido>().AddRange(
                OrderHistory(delivered, admin, EstadoPedidoPersonalizado.ENTREGADO, Utc(2026, 1, 16)),
                OrderHistory(cancelled, admin, EstadoPedidoPersonalizado.CANCELADO, Utc(2026, 7, 2)));
            var advance = Payment(delivered, admin, "PAG-1", TipoPagoPedido.ANTICIPO, 3_000, 7_000, 4_000, Utc(2026, 1, 12));
            var final = Payment(delivered, admin, "PAG-2", TipoPagoPedido.PAGO_FINAL, 4_000, 4_000, 0, Utc(2026, 1, 16));
            context.AddRange(advance, final);
            await context.SaveChangesAsync();
            context.ReembolsosPedidoPersonalizado.Add(new ReembolsoPedidoPersonalizado
            {
                CodigoReembolso = "REE-1",
                OperacionId = Guid.NewGuid(),
                PedidoPersonalizadoId = delivered.PedidoPersonalizadoId,
                PagoPedidoPersonalizadoId = final.PagoPedidoPersonalizadoId,
                MetodoPago = MetodoPago.EFECTIVO,
                MontoCentavos = 500,
                Motivo = "Ajuste comercial",
                Fecha = Utc(2026, 1, 20),
                UsuarioId = admin.UsuarioId,
                SaldoAnteriorCentavos = 0,
                SaldoNuevoCentavos = 500,
                FechaCreacion = Utc(2026, 1, 20)
            });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var session = new TestSession(new UsuarioSesion(
                admin.UsuarioId, admin.NombreCompleto, admin.NombreUsuario, admin.Rol));
            var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 15, 18, 0, 0, TimeSpan.Zero));
            var sales = new SalesAnalyticsQueryService(context, session, clock);
            var service = new CommercialAnalyticsQueryService(context, sales, clock);
            return new CommercialFixture(connection, context, service);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private static Cotizacion Quote(
            string code, Cliente customer, Usuario admin, DateTime date, long total, EstadoCotizacion state) => new()
        {
            CodigoCotizacion = code,
            ClienteId = customer.ClienteId,
            NombreDestinatarioSnapshot = customer.NombreCompleto,
            Fecha = date,
            FechaVencimiento = date.AddDays(30),
            Estado = state,
            SubtotalCentavos = total,
            TotalCentavos = total,
            UsuarioCreadorId = admin.UsuarioId,
            UsuarioUltimaActualizacionId = admin.UsuarioId,
            FechaCreacion = date,
            FechaActualizacion = date
        };

        private static HistorialEstadoCotizacion QuoteHistory(
            Cotizacion quote, Usuario admin, EstadoCotizacion state, DateTime date) => new()
        {
            CotizacionId = quote.CotizacionId,
            EstadoNuevo = state,
            UsuarioId = admin.UsuarioId,
            Fecha = date
        };

        private static PedidoPersonalizado Order(
            string code, Cliente customer, Usuario admin, DateTime date, DateTime promised,
            EstadoPedidoPersonalizado state, long total, long outstanding) => new()
        {
            CodigoPedido = code,
            ClienteId = customer.ClienteId,
            NombreDestinatarioSnapshot = customer.NombreCompleto,
            Estado = state,
            Prioridad = PrioridadPedido.NORMAL,
            FechaPedido = date,
            FechaPrometida = promised,
            SubtotalCentavos = total,
            TotalEstimadoCentavos = total,
            EstadoFinanciero = outstanding == 0 ? EstadoFinancieroPedido.PAGADO : EstadoFinancieroPedido.SIN_PAGO,
            TotalPagadoCentavos = total - outstanding,
            SaldoPendienteCentavos = outstanding,
            UsuarioCreadorId = admin.UsuarioId,
            UsuarioUltimaActualizacionId = admin.UsuarioId,
            FechaCreacion = date,
            FechaActualizacion = date
        };

        private static HistorialEstadoPedido OrderHistory(
            PedidoPersonalizado order, Usuario admin, EstadoPedidoPersonalizado state, DateTime date) => new()
        {
            PedidoPersonalizadoId = order.PedidoPersonalizadoId,
            EstadoNuevo = state,
            UsuarioId = admin.UsuarioId,
            Fecha = date
        };

        private static PagoPedidoPersonalizado Payment(
            PedidoPersonalizado order, Usuario admin, string code, TipoPagoPedido type,
            long amount, long before, long after, DateTime date) => new()
        {
            CodigoPago = code,
            OperacionId = Guid.NewGuid(),
            PedidoPersonalizadoId = order.PedidoPersonalizadoId,
            TipoPago = type,
            MetodoPago = MetodoPago.EFECTIVO,
            MontoCentavos = amount,
            Fecha = date,
            UsuarioId = admin.UsuarioId,
            SaldoAnteriorCentavos = before,
            SaldoNuevoCentavos = after,
            FechaCreacion = date
        };

        private static DateTime Utc(int year, int month, int day, int hour = 12) =>
            new(year, month, day, hour, 0, 0, DateTimeKind.Utc);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
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
