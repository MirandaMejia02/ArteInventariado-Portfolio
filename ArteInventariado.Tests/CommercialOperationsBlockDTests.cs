using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class CommercialOperationsBlockDTests
{
    [Fact]
    public async Task CashMovements_SeparatesPhysicalAndElectronic_WithoutDuplicatingAppliedOrderPayment()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.CashMovements.SearchAsync(new(DateTime.Today, DateTime.Today));

        Assert.Equal(3, result.Movements.Count(x => x.Origen == CashMovementOrigin.PEDIDO));
        Assert.Equal(1000, result.Summary.AnticiposPedidosEfectivoCentavos);
        Assert.Equal(500, result.Summary.TransferenciasRecibidasCentavos);
        Assert.Equal(250, result.Summary.BitcoinRecibidoCentavos);
        Assert.Equal(200, result.Summary.ReembolsosPedidosEfectivoCentavos);
        Assert.Equal(300, result.Summary.PagosEmpleadosCentavos);
        Assert.Equal(10_350, result.Summary.EfectivoEsperadoCentavos);
        var employeePayments = result.Movements.Where(x => x.Origen == CashMovementOrigin.PAGO_EMPLEADO).ToList();
        Assert.Equal(2, employeePayments.Count);
        Assert.Single(employeePayments, x => x.AfectaEfectivo);
        Assert.Contains(employeePayments, x => !x.AfectaEfectivo && x.Descripcion.Contains("Dueño", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Movements, x => x.Documento == "VTA-DUPLICADA");
    }

    [Fact]
    public async Task CommercialAlerts_AreCalculatedAndDisappearWhenSourceIsResolved()
    {
        await using var fixture = await Fixture.CreateAsync();

        var initial = await fixture.Alerts.SearchAsync(new());
        Assert.Contains(initial.Alerts, x => x.Tipo == CommercialAlertType.COTIZACION_VENCIDA);
        Assert.Contains(initial.Alerts, x => x.Tipo == CommercialAlertType.PEDIDO_ATRASADO);
        Assert.Contains(initial.Alerts, x => x.Tipo == CommercialAlertType.PEDIDO_LISTO_ENTREGA);
        Assert.Contains(initial.Alerts, x => x.Tipo == CommercialAlertType.PEDIDO_CANCELADO_REEMBOLSO_PENDIENTE);

        fixture.Quotation.Estado = EstadoCotizacion.ACEPTADA;
        fixture.CancelledOrder.TotalReembolsadoCentavos = fixture.CancelledOrder.TotalPagadoCentavos;
        await fixture.Context.SaveChangesAsync();

        var resolved = await fixture.Alerts.SearchAsync(new());
        Assert.DoesNotContain(resolved.Alerts, x => x.Documento == fixture.Quotation.CodigoCotizacion);
        Assert.DoesNotContain(resolved.Alerts, x =>
            x.Documento == fixture.CancelledOrder.CodigoPedido &&
            x.Tipo == CommercialAlertType.PEDIDO_CANCELADO_REEMBOLSO_PENDIENTE);
    }

    [Fact]
    public async Task Reports_CreateAllCommercialAndCashSheets_WithDollarValues()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = new CommercialReportRequest(DateTime.Today, DateTime.Today);

        var commercial = await fixture.Reports.ExportCommercialOperationsAsync(request);
        var cash = await fixture.Reports.ExportCashDetailAsync(request);

        Assert.True(File.Exists(commercial.FilePath));
        Assert.True(File.Exists(cash.FilePath));
        using var commercialBook = new XLWorkbook(commercial.FilePath);
        Assert.Equal(
            ["Cotizaciones", "Pedidos", "Pagos pedidos", "Reembolsos", "Reservas", "Conversiones", "Alertas actuales"],
            commercialBook.Worksheets.Select(x => x.Name).ToArray());
        using var cashBook = new XLWorkbook(cash.FilePath);
        Assert.Equal(["Resumen de caja", "Movimientos"], cashBook.Worksheets.Select(x => x.Name).ToArray());
        var expectedRow = cashBook.Worksheet("Resumen de caja").Column(1).CellsUsed()
            .Single(x => x.GetString() == "Efectivo esperado").Address.RowNumber;
        Assert.Equal(103.50m, cashBook.Worksheet("Resumen de caja").Cell(expectedRow, 2).GetValue<decimal>());
    }

    [Fact]
    public void PermissionCatalog_ContainsBlockDPermissions_AndEmployeeMenuRequiresThem()
    {
        Assert.Contains(EmployeePermissionCatalog.All, x => x.Permiso == PermisoEmpleado.ALERTAS_COMERCIALES_VER);
        Assert.Contains(EmployeePermissionCatalog.All, x => x.Permiso == PermisoEmpleado.REPORTES_COMERCIALES_VER);
        Assert.Contains(EmployeePermissionCatalog.All, x => x.Permiso == PermisoEmpleado.CAJA_MOVIMIENTOS_DETALLE_VER);
        var menu = EmployeeDashboardMoreMenuCatalog.Build(new Dictionary<PermisoEmpleado, bool>
        {
            [PermisoEmpleado.ALERTAS_COMERCIALES_VER] = true,
            [PermisoEmpleado.REPORTES_COMERCIALES_VER] = false
        });
        Assert.Contains(menu, x => x.Action == EmployeeDashboardMoreAction.AlertasComerciales);
        Assert.DoesNotContain(menu, x => x.Action == EmployeeDashboardMoreAction.ReportesComerciales);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly string _outputDirectory;

        private Fixture(
            SqliteConnection connection,
            AppDbContext context,
            CommercialAlertQueryService alerts,
            CashMovementQueryService cashMovements,
            CommercialOperationsReportService reports,
            Cotizacion quotation,
            PedidoPersonalizado cancelledOrder,
            string outputDirectory)
        {
            _connection = connection;
            Context = context;
            Alerts = alerts;
            CashMovements = cashMovements;
            Reports = reports;
            Quotation = quotation;
            CancelledOrder = cancelledOrder;
            _outputDirectory = outputDirectory;
        }

        public AppDbContext Context { get; }
        public CommercialAlertQueryService Alerts { get; }
        public CashMovementQueryService CashMovements { get; }
        public CommercialOperationsReportService Reports { get; }
        public Cotizacion Quotation { get; }
        public PedidoPersonalizado CancelledOrder { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();

            var user = new Usuario
            {
                NombreCompleto = "Administrador Block D",
                NombreUsuario = "admin-block-d",
                PasswordHash = "hash",
                Rol = RolUsuario.ADMIN,
                Activo = true
            };
            context.Usuarios.Add(user);
            await context.SaveChangesAsync();
            var session = new CurrentSession();
            session.IniciarSesion(new UsuarioSesion(user.UsuarioId, user.NombreCompleto, user.NombreUsuario, user.Rol));
            var audit = new AuditService(context);
            var permissions = new EmployeePermissionService(context, session, audit);

            var cash = new Caja
            {
                FechaOperacion = DateOnly.FromDateTime(DateTime.Today),
                FechaApertura = DateTime.UtcNow.AddHours(-2),
                MontoInicialCentavos = 10_000,
                Estado = EstadoCaja.ABIERTA,
                UsuarioAperturaId = user.UsuarioId
            };
            context.Cajas.Add(cash);

            var quotation = new Cotizacion
            {
                CodigoCotizacion = "COT-BD-001",
                NombreDestinatarioSnapshot = "Cliente vencido",
                Fecha = DateTime.UtcNow.AddDays(-8),
                FechaVencimiento = DateTime.Today.AddDays(-1),
                FechaEmision = DateTime.UtcNow.AddDays(-7),
                Estado = EstadoCotizacion.EMITIDA,
                SubtotalCentavos = 2000,
                TotalCentavos = 2000,
                UsuarioCreadorId = user.UsuarioId,
                UsuarioUltimaActualizacionId = user.UsuarioId
            };
            var readyOrder = NewOrder("PED-BD-001", EstadoPedidoPersonalizado.LISTO_PARA_ENTREGA, user.UsuarioId);
            readyOrder.FechaPrometida = DateTime.Today.AddDays(-1);
            readyOrder.Detalles.Add(new DetallePedidoPersonalizado
            {
                NombreDescripcionSnapshot = "Trabajo descriptivo",
                Cantidad = 1,
                PrecioUnitarioCentavos = 2000,
                SubtotalCentavos = 2000
            });
            var cancelledOrder = NewOrder("PED-BD-002", EstadoPedidoPersonalizado.CANCELADO, user.UsuarioId);
            cancelledOrder.TotalPagadoCentavos = 1000;
            cancelledOrder.TotalReembolsadoCentavos = 200;
            cancelledOrder.SaldoPendienteCentavos = 0;
            context.AddRange(quotation, readyOrder, cancelledOrder);
            await context.SaveChangesAsync();

            var payments = new[]
            {
                NewOrderPayment("PAG-BD-001", readyOrder, user, cash, TipoPagoPedido.ANTICIPO, MetodoPago.EFECTIVO, 1000, true),
                NewOrderPayment("PAG-BD-002", readyOrder, user, null, TipoPagoPedido.ABONO, MetodoPago.TRANSFERENCIA, 500, false),
                NewOrderPayment("PAG-BD-003", readyOrder, user, null, TipoPagoPedido.PAGO_FINAL, MetodoPago.BITCOIN, 250, false)
            };
            context.PagosPedidoPersonalizado.AddRange(payments);
            context.ReembolsosPedidoPersonalizado.Add(new ReembolsoPedidoPersonalizado
            {
                CodigoReembolso = "REE-BD-001",
                OperacionId = Guid.NewGuid(),
                PedidoPersonalizadoId = readyOrder.PedidoPersonalizadoId,
                MetodoPago = MetodoPago.EFECTIVO,
                MontoCentavos = 200,
                Motivo = "Ajuste",
                Fecha = DateTime.UtcNow,
                UsuarioId = user.UsuarioId,
                AfectaCaja = true,
                CajaId = cash.CajaId,
                SaldoAnteriorCentavos = 500,
                SaldoNuevoCentavos = 300,
                FechaCreacion = DateTime.UtcNow
            });
            context.Gastos.Add(new Gasto { CajaId = cash.CajaId, UsuarioId = user.UsuarioId, Concepto = "Gasto prueba", MontoCentavos = 100, FechaGasto = DateTime.UtcNow });
            context.RetirosCaja.Add(new RetiroCaja { CajaId = cash.CajaId, UsuarioId = user.UsuarioId, Motivo = "Retiro prueba", MontoCentavos = 50, FechaRetiro = DateTime.UtcNow });
            context.EmployeePayments.AddRange(
                new EmployeePayment
                {
                    UserId = user.UsuarioId,
                    CajaId = cash.CajaId,
                    AmountCentavos = 300,
                    PaymentType = EmployeePaymentType.Daily,
                    MetodoPago = MetodoPago.EFECTIVO,
                    OrigenFondos = OrigenFondos.CAJA_FISICA,
                    OperacionId = Guid.NewGuid(),
                    RegisteredByUserId = user.UsuarioId,
                    CreatedAtUtc = DateTime.UtcNow,
                    Status = EmployeePaymentStatus.Active
                },
                new EmployeePayment
                {
                    UserId = user.UsuarioId,
                    AmountCentavos = 200,
                    PaymentType = EmployeePaymentType.Extraordinary,
                    MetodoPago = MetodoPago.EFECTIVO,
                    OrigenFondos = OrigenFondos.FONDOS_EXTERNOS_DUENO,
                    ResponsableOrigen = "Dueño",
                    OperacionId = Guid.NewGuid(),
                    RegisteredByUserId = user.UsuarioId,
                    CreatedAtUtc = DateTime.UtcNow,
                    Status = EmployeePaymentStatus.Active
                });
            await context.SaveChangesAsync();

            var duplicateSale = new Venta
            {
                NumeroVenta = "VTA-DUPLICADA",
                CajaId = cash.CajaId,
                UsuarioId = user.UsuarioId,
                FechaVenta = DateTime.UtcNow,
                SubtotalCentavos = 1000,
                TotalCentavos = 1000,
                PagadoCentavos = 1000
            };
            context.Ventas.Add(duplicateSale);
            await context.SaveChangesAsync();
            context.PagosVenta.Add(new PagoVenta
            {
                VentaId = duplicateSale.VentaId,
                MetodoPago = MetodoPago.EFECTIVO,
                MontoCentavos = 1000,
                FechaPago = DateTime.UtcNow,
                Origen = OrigenPagoVenta.PEDIDO,
                PagoPedidoPersonalizadoId = payments[0].PagoPedidoPersonalizadoId,
                AfectaCaja = false
            });
            await context.SaveChangesAsync();

            var alerts = new CommercialAlertQueryService(context, permissions);
            var movements = new CashMovementQueryService(context, permissions);
            var output = Path.Combine(Path.GetTempPath(), $"ArteInventariado-BlockD-{Guid.NewGuid():N}");
            var reports = new CommercialOperationsReportService(
                context, session, permissions, audit, new TempReportPaths(output), alerts, movements);
            return new(connection, context, alerts, movements, reports, quotation, cancelledOrder, output);
        }

        private static PedidoPersonalizado NewOrder(string code, EstadoPedidoPersonalizado state, int userId) => new()
        {
            CodigoPedido = code,
            NombreDestinatarioSnapshot = "Cliente pedido",
            Estado = state,
            Prioridad = PrioridadPedido.NORMAL,
            FechaPedido = DateTime.UtcNow.AddDays(-5),
            FechaPrometida = DateTime.Today.AddDays(2),
            SubtotalCentavos = 2000,
            TotalEstimadoCentavos = 2000,
            SaldoPendienteCentavos = 2000,
            UsuarioCreadorId = userId,
            UsuarioUltimaActualizacionId = userId,
            FechaCreacion = DateTime.UtcNow.AddDays(-5),
            FechaActualizacion = DateTime.UtcNow.AddDays(-4)
        };

        private static PagoPedidoPersonalizado NewOrderPayment(
            string code, PedidoPersonalizado order, Usuario user, Caja? cash,
            TipoPagoPedido type, MetodoPago method, long amount, bool affectsCash) => new()
        {
            CodigoPago = code,
            OperacionId = Guid.NewGuid(),
            PedidoPersonalizadoId = order.PedidoPersonalizadoId,
            TipoPago = type,
            MetodoPago = method,
            MontoCentavos = amount,
            Referencia = method == MetodoPago.EFECTIVO ? null : $"REF-{code}",
            Fecha = DateTime.UtcNow,
            UsuarioId = user.UsuarioId,
            AfectaCaja = affectsCash,
            CajaId = cash?.CajaId,
            SaldoAnteriorCentavos = 2000,
            SaldoNuevoCentavos = 2000 - amount,
            FechaCreacion = DateTime.UtcNow
        };

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
            if (Directory.Exists(_outputDirectory)) Directory.Delete(_outputDirectory, recursive: true);
        }
    }

    private sealed class TempReportPaths(string root) : IReportPathProvider
    {
        public Task<string> GetRootDirectoryAsync(CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(root);
            return Task.FromResult(root);
        }

        public async Task<string> GetDirectoryAsync(string category, CancellationToken cancellationToken = default)
        {
            var directory = Path.Combine(await GetRootDirectoryAsync(cancellationToken), category);
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
