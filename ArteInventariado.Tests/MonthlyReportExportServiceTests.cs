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

public class MonthlyReportExportServiceTests
{
    [Fact]
    public async Task ExportMonthlyReportAsync_IncludesCustomerAndSupplierFinancialSheets()
    {
        await using var fixture = await ReportFixture.CreateAsync();
        await fixture.SeedCreditScenarioAsync();
        await fixture.SeedSupplierFinanceScenarioAsync();

        var result = await fixture.Service.ExportMonthlyReportAsync(new MonthlyReportExportRequest(
            DateTime.Now.Year,
            DateTime.Now.Month));

        Assert.True(File.Exists(result.FilePath));
        using var workbook = new XLWorkbook(result.FilePath);
        Assert.True(workbook.Worksheets.Contains("Ventas a credito"));
        Assert.True(workbook.Worksheets.Contains("Abonos"));
        Assert.True(workbook.Worksheets.Contains("Cartera actual"));
        Assert.True(workbook.Worksheets.Contains("Compras"));
        Assert.True(workbook.Worksheets.Contains("Recepciones"));
        Assert.True(workbook.Worksheets.Contains("Pagos a proveedores"));
        Assert.True(workbook.Worksheets.Contains("Pagos a empleados"));
        Assert.True(workbook.Worksheets.Contains("Pagos fuera de caja"));
        Assert.True(workbook.Worksheets.Contains("Cuentas por pagar"));
        Assert.True(workbook.Worksheets.Contains("Historial de costos"));

        var creditSheet = workbook.Worksheet("Ventas a credito");
        Assert.Equal("CXC-REPORTE-0001", creditSheet.Cell(2, 3).GetString());
        Assert.Equal(15m, creditSheet.Cell(2, 5).GetValue<decimal>());
        Assert.Equal(5m, creditSheet.Cell(2, 8).GetValue<decimal>());

        var paymentsSheet = workbook.Worksheet("Abonos");
        Assert.Equal("ABN-REPORTE-0001", paymentsSheet.Cell(2, 2).GetString());
        Assert.Equal(3m, paymentsSheet.Cell(2, 7).GetValue<decimal>());
        Assert.Equal(1m, paymentsSheet.Cell(2, 8).GetValue<decimal>());
        Assert.Equal(2m, paymentsSheet.Cell(2, 9).GetValue<decimal>());

        var portfolioSheet = workbook.Worksheet("Cartera actual");
        Assert.Equal("CXC-REPORTE-0001", portfolioSheet.Cell(2, 2).GetString());
        Assert.Equal(12.50m, portfolioSheet.Cell(2, 7).GetValue<decimal>());

        var purchasesSheet = workbook.Worksheet("Compras");
        Assert.Equal("CMP-REPORTE-0001", purchasesSheet.Cell(2, 2).GetString());
        Assert.Equal(14m, purchasesSheet.Cell(2, 9).GetValue<decimal>());
        Assert.Equal(5m, purchasesSheet.Cell(2, 10).GetValue<decimal>());
        Assert.Equal(9m, purchasesSheet.Cell(2, 11).GetValue<decimal>());

        var supplierPaymentsSheet = workbook.Worksheet("Pagos a proveedores");
        Assert.Equal("PGC-REPORTE-0001", supplierPaymentsSheet.Cell(2, 3).GetString());
        Assert.Equal("CPP-REPORTE-0001", supplierPaymentsSheet.Cell(2, 6).GetString());
        Assert.Equal("TRANSFERENCIA", supplierPaymentsSheet.Cell(2, 7).GetString());
        Assert.Equal(3m, supplierPaymentsSheet.Cell(2, 8).GetValue<decimal>());
        Assert.Equal("No especificado (histórico)", supplierPaymentsSheet.Cell(2, 14).GetString());
        Assert.Equal("Desconocido (histórico)", supplierPaymentsSheet.Cell(2, 16).GetString());
        Assert.Equal("ABP-REPORTE-0001", supplierPaymentsSheet.Cell(3, 3).GetString());
        Assert.Equal("BITCOIN", supplierPaymentsSheet.Cell(3, 7).GetString());
        Assert.Equal(2m, supplierPaymentsSheet.Cell(3, 8).GetValue<decimal>());

        var payableSheet = workbook.Worksheet("Cuentas por pagar");
        Assert.Equal("CPP-REPORTE-0001", payableSheet.Cell(2, 1).GetString());
        Assert.Equal(9m, payableSheet.Cell(2, 8).GetValue<decimal>());
    }

    [Fact]
    public async Task ExportMonthlyReportAsync_DistinguishesExternalExpenseFromCashExpense()
    {
        await using var fixture = await ReportFixture.CreateAsync();
        var now = DateTime.UtcNow;
        fixture.Context.Gastos.AddRange(
            new Gasto
            {
                CajaId = fixture.CashId,
                UsuarioId = fixture.UserId,
                Concepto = "Pago desde cajón",
                MontoCentavos = 1_000,
                FechaGasto = now,
                MetodoPago = MetodoPago.EFECTIVO,
                OrigenFondos = OrigenFondos.CAJA_FISICA,
                OperacionId = Guid.NewGuid()
            },
            new Gasto
            {
                CajaId = null,
                UsuarioId = fixture.UserId,
                Concepto = "Pago por dueño",
                MontoCentavos = 2_000,
                FechaGasto = now.AddMinutes(1),
                MetodoPago = MetodoPago.EFECTIVO,
                OrigenFondos = OrigenFondos.FONDOS_EXTERNOS_DUENO,
                ResponsableOrigen = "Dueño",
                OperacionId = Guid.NewGuid()
            });
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.Service.ExportMonthlyReportAsync(new MonthlyReportExportRequest(
            DateTime.Now.Year,
            DateTime.Now.Month));

        using var workbook = new XLWorkbook(result.FilePath);
        var sheet = workbook.Worksheet("Gastos y retiros");
        Assert.Equal("Origen de fondos", sheet.Cell(1, 7).GetString());
        var cashRow = sheet.RowsUsed().Single(row => row.Cell(5).GetString() == "Pago desde cajón");
        Assert.Equal("Caja física", cashRow.Cell(7).GetString());
        Assert.Equal("Sí", cashRow.Cell(9).GetString());
        var externalRow = sheet.RowsUsed().Single(row => row.Cell(5).GetString() == "Pago por dueño");
        Assert.Equal("Fondos externos del dueño", externalRow.Cell(7).GetString());
        Assert.Equal("Dueño", externalRow.Cell(8).GetString());
        Assert.Equal("No", externalRow.Cell(9).GetString());

        var outsideCashSheet = workbook.Worksheet("Pagos fuera de caja");
        var externalExpenseRow = outsideCashSheet.RowsUsed().Single(row => row.Cell(4).GetString().StartsWith("GAS-", StringComparison.Ordinal));
        Assert.Equal("Gasto", externalExpenseRow.Cell(2).GetString());
        Assert.Equal("Fondos externos del dueño", externalExpenseRow.Cell(8).GetString());
        Assert.Equal("No", externalExpenseRow.Cell(12).GetString());
        Assert.DoesNotContain(outsideCashSheet.RowsUsed(), row => row.Cell(4).GetString().StartsWith("GAS-", StringComparison.Ordinal) && row.Cell(6).GetString() == "Pago desde cajón");
    }

    [Fact]
    public async Task ExportMonthlyReportAsync_ConsolidatesKnownExternalPaymentsAndDoesNotInferLegacyOrigins()
    {
        await using var fixture = await ReportFixture.CreateAsync();
        await fixture.SeedSupplierFinanceScenarioAsync();
        var now = DateTime.UtcNow;

        var initial = await fixture.Context.PagosCompra.SingleAsync(payment => payment.CodigoPago == "PGC-REPORTE-0001");
        initial.OrigenFondos = OrigenFondos.FONDOS_EXTERNOS_DUENO;
        initial.ResponsableOrigen = "Dueño del negocio";
        var installment = await fixture.Context.AbonosCuentaPorPagar.SingleAsync(payment => payment.CodigoAbono == "ABP-REPORTE-0001");
        installment.OrigenFondos = OrigenFondos.FONDOS_NEGOCIO_FUERA_CAJA;
        installment.ResponsableOrigen = "Cuenta del negocio";

        fixture.Context.Gastos.AddRange(
            new Gasto
            {
                CajaId = null,
                UsuarioId = fixture.UserId,
                Concepto = "Material para inventario",
                MontoCentavos = 2_000,
                FechaGasto = now,
                MetodoPago = MetodoPago.TRANSFERENCIA,
                OrigenFondos = OrigenFondos.FONDOS_EXTERNOS_DUENO,
                ResponsableOrigen = "Dueño del negocio",
                OperacionId = Guid.NewGuid()
            },
            new Gasto
            {
                CajaId = null,
                UsuarioId = fixture.UserId,
                Concepto = "Gasto histórico sin origen",
                MontoCentavos = 900,
                FechaGasto = now.AddMinutes(1),
                MetodoPago = MetodoPago.TRANSFERENCIA,
                OrigenFondos = null,
                ResponsableOrigen = null
            });

        var employee = new Usuario
        {
            NombreCompleto = "Empleado de prueba",
            NombreUsuario = "empleado-prueba",
            PasswordHash = "hash",
            Rol = RolUsuario.EMPLEADO,
            Activo = true,
            FechaCreacion = now
        };
        fixture.Context.Usuarios.Add(employee);
        await fixture.Context.SaveChangesAsync();

        fixture.Context.EmployeePayments.AddRange(
            new EmployeePayment
            {
                UserId = employee.UsuarioId,
                CajaId = null,
                AmountCentavos = 2_500,
                PaymentType = EmployeePaymentType.Weekly,
                MetodoPago = MetodoPago.TRANSFERENCIA,
                Referencia = "TRX-EMPLEADO",
                OperacionId = Guid.NewGuid(),
                OrigenFondos = OrigenFondos.FONDOS_EXTERNOS_DUENO,
                ResponsableOrigen = "Dueño del negocio",
                PeriodStart = DateOnly.FromDateTime(now.AddDays(-7)),
                PeriodEnd = DateOnly.FromDateTime(now),
                DaysConsidered = 7,
                Note = "Pago semanal",
                RegisteredByUserId = fixture.UserId,
                CreatedAtUtc = now,
                Status = EmployeePaymentStatus.Active
            },
            new EmployeePayment
            {
                UserId = employee.UsuarioId,
                CajaId = null,
                AmountCentavos = 700,
                PaymentType = EmployeePaymentType.Extraordinary,
                MetodoPago = MetodoPago.EFECTIVO,
                OperacionId = Guid.NewGuid(),
                OrigenFondos = OrigenFondos.FONDOS_NEGOCIO_FUERA_CAJA,
                ResponsableOrigen = "Cuenta del negocio",
                Note = "Pago anulado de prueba",
                RegisteredByUserId = fixture.UserId,
                CreatedAtUtc = now.AddMinutes(2),
                Status = EmployeePaymentStatus.Cancelled,
                CancelledByUserId = fixture.UserId,
                CancelledAtUtc = now.AddMinutes(3),
                CancellationReason = "Registro duplicado"
            });

        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var result = await fixture.Service.ExportMonthlyReportAsync(new MonthlyReportExportRequest(
            DateTime.Now.Year,
            DateTime.Now.Month));

        using var workbook = new XLWorkbook(result.FilePath);
        var outsideCashSheet = workbook.Worksheet("Pagos fuera de caja");
        Assert.Equal(50m, outsideCashSheet.Cell(3, 2).GetValue<decimal>());
        Assert.Equal(5m, outsideCashSheet.Cell(4, 2).GetValue<decimal>());
        Assert.Equal(20m, outsideCashSheet.Cell(5, 2).GetValue<decimal>());
        Assert.Equal(25m, outsideCashSheet.Cell(6, 2).GetValue<decimal>());
        Assert.Equal(7m, outsideCashSheet.Cell(9, 2).GetValue<decimal>());

        var externalRows = outsideCashSheet.RowsUsed()
            .Where(row => row.RowNumber() >= 13)
            .ToList();
        Assert.Equal(5, externalRows.Count);
        Assert.DoesNotContain(externalRows, row => row.Cell(6).GetString() == "Gasto histórico sin origen");
        var externalEmployee = externalRows.Single(row => row.Cell(4).GetString() == "EMP-000001");
        Assert.Equal("Fondos externos del dueño", externalEmployee.Cell(8).GetString());
        Assert.Equal("No", externalEmployee.Cell(12).GetString());
        var cancelledEmployee = externalRows.Single(row => row.Cell(4).GetString() == "EMP-000002");
        Assert.Contains("Anulado", cancelledEmployee.Cell(11).GetString());
        Assert.Contains("sin reverso automático", cancelledEmployee.Cell(11).GetString());

        var employeeSheet = workbook.Worksheet("Pagos a empleados");
        var employeeRow = employeeSheet.RowsUsed().Single(row => row.Cell(2).GetString() == "EMP-000001");
        Assert.Equal("Empleado de prueba", employeeRow.Cell(3).GetString());
        Assert.Equal("Fondos externos del dueño", employeeRow.Cell(11).GetString());
        Assert.Equal("No", employeeRow.Cell(13).GetString());
        Assert.Equal("Activo", employeeRow.Cell(14).GetString());

        var supplierSheet = workbook.Worksheet("Pagos a proveedores");
        var externalSupplierRow = supplierSheet.RowsUsed().Single(row => row.Cell(3).GetString() == "PGC-REPORTE-0001");
        Assert.Equal("Fondos externos del dueño", externalSupplierRow.Cell(14).GetString());
        Assert.Equal("Dueño del negocio", externalSupplierRow.Cell(15).GetString());
        Assert.Equal("No", externalSupplierRow.Cell(16).GetString());
    }

    private sealed class ReportFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly string _reportDirectory;

        private ReportFixture(
            SqliteConnection connection,
            AppDbContext context,
            MonthlyReportExportService service,
            string reportDirectory,
            int userId,
            int cashId)
        {
            _connection = connection;
            Context = context;
            Service = service;
            _reportDirectory = reportDirectory;
            UserId = userId;
            CashId = cashId;
        }

        public AppDbContext Context { get; }
        public MonthlyReportExportService Service { get; }
        public int UserId { get; }
        public int CashId { get; }

        public static async Task<ReportFixture> CreateAsync()
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
                NombreCompleto = "Admin reportes",
                NombreUsuario = "admin-reportes",
                PasswordHash = "hash",
                Rol = RolUsuario.ADMIN,
                Activo = true,
                FechaCreacion = DateTime.UtcNow
            };
            context.Usuarios.Add(user);
            await context.SaveChangesAsync();

            var cash = new Caja
            {
                FechaOperacion = DateOnly.FromDateTime(DateTime.Now),
                FechaApertura = DateTime.UtcNow,
                MontoInicialCentavos = 0,
                MontoCierreCentavos = 0,
                Estado = EstadoCaja.ABIERTA,
                UsuarioAperturaId = user.UsuarioId
            };
            context.Cajas.Add(cash);

            var reportDirectory = Path.Combine(Path.GetTempPath(), $"arte-report-{Guid.NewGuid():N}");
            context.ConfiguracionSistema.Add(new ConfiguracionSistema
            {
                Clave = SystemConfigService.ReportsFolderKey,
                Valor = reportDirectory,
                Descripcion = "Carpeta de pruebas para reportes.",
                FechaModificacion = DateTime.UtcNow
            });
            context.ConfiguracionSistema.Add(new ConfiguracionSistema
            {
                Clave = "credito.dias_mora",
                Valor = "7",
                Descripcion = "Dias mora.",
                FechaModificacion = DateTime.UtcNow
            });
            await context.SaveChangesAsync();

            var session = new TestCurrentSession(new UsuarioSesion(
                user.UsuarioId,
                user.NombreCompleto,
                user.NombreUsuario,
                user.Rol));
            var service = new MonthlyReportExportService(context, session, new AuditService(context));
            return new ReportFixture(connection, context, service, reportDirectory, user.UsuarioId, cash.CajaId);
        }

        public async Task SeedCreditScenarioAsync()
        {
            var customer = new Cliente
            {
                CodigoCliente = "CLI-REPORTE",
                NombreCompleto = "Cliente reporte",
                NombreNormalizado = "cliente reporte",
                Estado = EstadoCliente.ACTIVO,
                CreditoHabilitado = true,
                UsuarioCreacionId = UserId,
                FechaCreacion = DateTime.UtcNow,
                FechaActualizacion = DateTime.UtcNow
            };
            Context.Clientes.Add(customer);
            await Context.SaveChangesAsync();

            var sale = new Venta
            {
                NumeroVenta = "VTA-REPORTE-0001",
                CajaId = CashId,
                UsuarioId = UserId,
                ClienteId = customer.ClienteId,
                ClienteNombreSnapshot = customer.NombreCompleto,
                FechaVenta = DateTime.UtcNow,
                SubtotalCentavos = 2100,
                DescuentoCentavos = 0,
                TotalCentavos = 2100,
                PagadoCentavos = 500,
                CambioCentavos = 0,
                Estado = EstadoVenta.COMPLETADA
            };
            Context.Ventas.Add(sale);
            await Context.SaveChangesAsync();

            Context.PagosVenta.Add(new PagoVenta
            {
                VentaId = sale.VentaId,
                MetodoPago = MetodoPago.EFECTIVO,
                MontoCentavos = 500,
                FechaPago = DateTime.UtcNow
            });

            var account = new CuentaPorCobrar
            {
                CodigoCuenta = "CXC-REPORTE-0001",
                VentaId = sale.VentaId,
                ClienteId = customer.ClienteId,
                CajaId = CashId,
                UsuarioRegistroId = UserId,
                CapitalFinanciadoCentavos = 1500,
                InteresPorcentaje = 6.6667m,
                InteresCalculadoCentavos = 100,
                TotalOriginalCentavos = 1600,
                TotalPagadoCentavos = 350,
                SaldoActualCentavos = 1250,
                FechaCreacion = DateTime.UtcNow,
                FechaVencimiento = DateTime.UtcNow.AddDays(7),
                Estado = EstadoCuentaPorCobrar.ABIERTA,
                FechaActualizacion = DateTime.UtcNow
            };
            Context.CuentasPorCobrar.Add(account);
            await Context.SaveChangesAsync();

            Context.AbonosCuentaPorCobrar.AddRange(
                new AbonoCuentaPorCobrar
                {
                    CodigoAbono = "ABN-REPORTE-0001",
                    OperacionId = Guid.NewGuid(),
                    CuentaPorCobrarId = account.CuentaPorCobrarId,
                    UsuarioId = UserId,
                    CajaId = CashId,
                    MetodoPago = MetodoPago.EFECTIVO,
                    MontoCentavos = 300,
                    AplicadoInteresCentavos = 100,
                    AplicadoCapitalCentavos = 200,
                    SaldoAnteriorCentavos = 1600,
                    SaldoNuevoCentavos = 1300,
                    InteresPendienteAnteriorCentavos = 100,
                    InteresPendienteNuevoCentavos = 0,
                    CapitalPendienteAnteriorCentavos = 1500,
                    CapitalPendienteNuevoCentavos = 1300,
                    FechaAbono = DateTime.UtcNow
                },
                new AbonoCuentaPorCobrar
                {
                    CodigoAbono = "ABN-REPORTE-0002",
                    OperacionId = Guid.NewGuid(),
                    CuentaPorCobrarId = account.CuentaPorCobrarId,
                    UsuarioId = UserId,
                    MetodoPago = MetodoPago.TRANSFERENCIA,
                    MontoCentavos = 50,
                    AplicadoInteresCentavos = 0,
                    AplicadoCapitalCentavos = 50,
                    SaldoAnteriorCentavos = 1300,
                    SaldoNuevoCentavos = 1250,
                    InteresPendienteAnteriorCentavos = 0,
                    InteresPendienteNuevoCentavos = 0,
                    CapitalPendienteAnteriorCentavos = 1300,
                    CapitalPendienteNuevoCentavos = 1250,
                    Referencia = "TRX-REPORTE",
                    FechaAbono = DateTime.UtcNow
                });
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
        }

        public async Task SeedSupplierFinanceScenarioAsync()
        {
            var supplier = new Proveedor
            {
                CodigoProveedor = "PRV-REPORTE",
                Tipo = TipoProveedor.PROVEEDOR,
                NombreRazonSocial = "Proveedor reporte",
                NombreNormalizado = "proveedor reporte",
                Estado = EstadoProveedor.ACTIVO,
                UsuarioCreacionId = UserId,
                FechaCreacion = DateTime.UtcNow,
                FechaActualizacion = DateTime.UtcNow
            };
            Context.Proveedores.Add(supplier);
            await Context.SaveChangesAsync();

            var purchase = new Compra
            {
                CodigoCompra = "CMP-REPORTE-0001",
                ProveedorId = supplier.ProveedorId,
                UsuarioRegistroId = UserId,
                FechaCompra = DateTime.UtcNow,
                FechaRegistro = DateTime.UtcNow,
                EstadoRecepcion = EstadoRecepcionCompra.PENDIENTE,
                SubtotalCentavos = 1400,
                DescuentoCentavos = 0,
                TotalCentavos = 1400,
                EstadoFinanciero = EstadoFinancieroCompra.PARCIAL,
                TotalPagadoCentavos = 500,
                SaldoPendienteCentavos = 900,
                FechaActualizacion = DateTime.UtcNow
            };
            Context.Compras.Add(purchase);
            await Context.SaveChangesAsync();

            var account = new CuentaPorPagar
            {
                CodigoCuenta = "CPP-REPORTE-0001",
                CompraId = purchase.CompraId,
                ProveedorId = supplier.ProveedorId,
                UsuarioCreadorId = UserId,
                TotalOriginalCentavos = 1400,
                TotalPagadoCentavos = 500,
                SaldoPendienteCentavos = 900,
                FechaCreacion = DateTime.UtcNow,
                FechaVencimiento = DateTime.UtcNow.AddDays(20),
                Estado = EstadoCuentaPorPagar.PARCIAL,
                FechaActualizacion = DateTime.UtcNow
            };
            Context.CuentasPorPagar.Add(account);
            await Context.SaveChangesAsync();

            Context.PagosCompra.Add(new PagoCompra
            {
                CodigoPago = "PGC-REPORTE-0001",
                OperacionId = Guid.NewGuid(),
                CompraId = purchase.CompraId,
                UsuarioId = UserId,
                MetodoPago = MetodoPago.TRANSFERENCIA,
                MontoCentavos = 300,
                Referencia = "TRX-PROVEEDOR",
                FechaPago = DateTime.UtcNow.AddHours(-1),
                FechaCreacion = DateTime.UtcNow.AddHours(-1),
                EsPagoInicial = true
            });
            Context.AbonosCuentaPorPagar.Add(new AbonoCuentaPorPagar
            {
                CodigoAbono = "ABP-REPORTE-0001",
                OperacionId = Guid.NewGuid(),
                CuentaPorPagarId = account.CuentaPorPagarId,
                UsuarioId = UserId,
                MetodoPago = MetodoPago.BITCOIN,
                MontoCentavos = 200,
                Referencia = "BTC-PROVEEDOR",
                FechaAbono = DateTime.UtcNow,
                SaldoAnteriorCentavos = 1100,
                SaldoNuevoCentavos = 900
            });
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
            if (Directory.Exists(_reportDirectory))
            {
                Directory.Delete(_reportDirectory, recursive: true);
            }
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
}
