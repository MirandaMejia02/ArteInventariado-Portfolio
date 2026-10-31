using System.Reflection;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Tests;

public sealed class GlobalPersistenceArchitectureTests
{
    [Fact]
    public void DependencyInjectionGraph_ResolvesAfterFactoryMigration()
    {
        var services = new ServiceCollection();
        services.AddArteInventariadoInfrastructure();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        using var scope = provider.CreateScope();

        var applicationServices = services
            .Where(descriptor => descriptor.ServiceType.Namespace == typeof(IAuditService).Namespace)
            .Select(descriptor => descriptor.ServiceType)
            .Distinct()
            .ToList();

        foreach (var serviceType in applicationServices)
        {
            Assert.NotNull(scope.ServiceProvider.GetRequiredService(serviceType));
        }
    }

    [Fact]
    public void PersistenceServices_ExposeOnlyFactoryBackedProductionConstructors()
    {
        var serviceTypes = typeof(AuditService).Assembly
            .GetTypes()
            .Where(type =>
                type is { IsClass: true, IsAbstract: false } &&
                type.Namespace == typeof(AuditService).Namespace &&
                type.Name.EndsWith("Service", StringComparison.Ordinal))
            .ToList();

        var publicContextConstructors = serviceTypes
            .SelectMany(type => type.GetConstructors(BindingFlags.Instance | BindingFlags.Public))
            .Where(constructor => constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(AppDbContext)))
            .Select(constructor => constructor.DeclaringType!.Name)
            .OrderBy(name => name)
            .ToList();

        Assert.Empty(publicContextConstructors);

        var contextAwareServicesWithoutFactory = serviceTypes
            .Where(type => type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Any(field => field.FieldType == typeof(AppDbContext)))
            .Where(type => !type.GetConstructors(BindingFlags.Instance | BindingFlags.Public)
                .Any(constructor => constructor.GetParameters()
                    .Any(parameter => parameter.ParameterType == typeof(IDbContextFactory<AppDbContext>))))
            .Select(type => type.Name)
            .OrderBy(name => name)
            .ToList();

        Assert.Empty(contextAwareServicesWithoutFactory);
    }

    [Fact]
    public async Task NestedAudit_UsesCommandTransaction_AndRollbackDoesNotPoisonNextOperation()
    {
        await using var fixture = await FactoryFixture.CreateAsync();
        var audit = new AuditService(fixture.Factory);

        await using (var lease = await AppDbContextLease.CreateAsync(
            fixture.Factory, legacyContext: null, CancellationToken.None))
        await using (var transaction = await lease.Context.Database.BeginTransactionAsync())
        {
            await audit.RegistrarAsync(new AuditEntryRequest(
                fixture.AdminId,
                TipoBitacora.CONFIGURACION_MODIFICADA,
                "global_transaction_test",
                1,
                "Esta entrada debe revertirse."));

            Assert.Single(lease.Context.ChangeTracker.Entries<Bitacora>());
            await transaction.RollbackAsync();
        }

        await using (var verification = fixture.Factory.CreateDbContext())
        {
            Assert.Empty(await verification.Bitacora
                .Where(entry => entry.Entidad == "global_transaction_test")
                .ToListAsync());
        }

        await audit.RegistrarAsync(new AuditEntryRequest(
            fixture.AdminId,
            TipoBitacora.CONFIGURACION_MODIFICADA,
            "global_transaction_test",
            2,
            "Esta entrada debe persistir."));

        await using var finalVerification = fixture.Factory.CreateDbContext();
        var stored = await finalVerification.Bitacora
            .Where(entry => entry.Entidad == "global_transaction_test")
            .ToListAsync();
        Assert.Single(stored);
        Assert.Equal(2, stored[0].EntidadId);
    }

    [Fact]
    public async Task ReadOperations_DoNotCreateConfigurationPermissionsOrAuditRows()
    {
        await using var fixture = await FactoryFixture.CreateAsync();
        var audit = new AuditService(fixture.Factory);
        var permissions = new EmployeePermissionService(fixture.Factory, fixture.Session, audit);
        var alerts = new CreditAlertQueryService(fixture.Factory, fixture.Session, permissions, audit);

        _ = await alerts.GetSettingsAsync();
        _ = await alerts.GetAlertsAsync();
        _ = await permissions.GetPermissionsForUserAsync(fixture.EmployeeId);

        await using var verification = fixture.Factory.CreateDbContext();
        Assert.Equal(0, await verification.ConfiguracionSistema.CountAsync());
        Assert.Equal(0, await verification.UsuarioPermisos.CountAsync());
        Assert.Equal(0, await verification.Bitacora.CountAsync());
    }

    [Fact]
    public async Task FactoryBackedServices_CompleteTwentyInterleavedPurchasesPaymentsAndPermissionChanges()
    {
        await using var fixture = await FactoryFixture.CreateAsync();
        var audit = new AuditService(fixture.Factory);
        var permissions = new EmployeePermissionService(fixture.Factory, fixture.Session, audit);
        var purchaseQuery = new PurchaseQueryService(fixture.Factory, permissions);
        var purchases = new PurchaseService(
            fixture.Factory,
            fixture.Session,
            permissions,
            audit,
            purchaseQuery);
        var payments = new AccountPaymentService(
            fixture.Factory,
            fixture.Session,
            permissions,
            audit);

        for (var index = 0; index < 20; index++)
        {
            var purchase = await purchases.CreateAsync(new CreatePurchaseRequest(
                fixture.SupplierId,
                DateTime.Today,
                [new PurchaseLineRequest(fixture.ProductId, 1, 100 + index)],
                Observaciones: $"Compra estable {index + 1}."));
            Assert.True(purchase.CompraId > 0);

            var payment = await payments.RegisterPaymentAsync(new RegisterAccountPaymentRequest(
                Guid.NewGuid(),
                fixture.AccountId,
                100,
                MetodoPago.TRANSFERENCIA,
                $"TRX-STABLE-{index + 1:00}",
                $"Abono estable {index + 1}."));
            Assert.Equal((19 - index) * 100, payment.SaldoNuevoCentavos);

            var customerAccessEnabled = index % 2 == 0;
            var permissionState = EmployeePermissionCatalog.All
                .Select(definition => new EmployeePermissionUpdate(
                    definition.Permiso,
                    definition.Permiso == PermisoEmpleado.CLIENTES_ACCEDER && customerAccessEnabled))
                .ToList();
            await permissions.UpdatePermissionsAsync(new UpdateEmployeePermissionsRequest(
                fixture.EmployeeId,
                permissionState));
        }

        await using var verification = fixture.Factory.CreateDbContext();
        Assert.Equal(20, await verification.Compras.CountAsync());
        Assert.Equal(20, await verification.CuentasPorPagar.CountAsync());
        Assert.Equal(20, await verification.AbonosCuentaPorCobrar.CountAsync());
        Assert.Equal(0, await verification.CuentasPorCobrar
            .Where(account => account.CuentaPorCobrarId == fixture.AccountId)
            .Select(account => account.SaldoActualCentavos)
            .SingleAsync());
        Assert.Equal(EstadoCuentaPorCobrar.PAGADA, await verification.CuentasPorCobrar
            .Where(account => account.CuentaPorCobrarId == fixture.AccountId)
            .Select(account => account.Estado)
            .SingleAsync());
        Assert.Equal(20, await verification.Bitacora
            .CountAsync(entry => entry.TipoAccion == TipoBitacora.PERMISOS_MODIFICADOS));
    }

    private sealed class FactoryFixture : IAsyncDisposable
    {
        private readonly string _databasePath;

        private FactoryFixture(
            string databasePath,
            FileContextFactory factory,
            CurrentSession session,
            int adminId,
            int employeeId,
            int supplierId,
            int productId,
            int accountId)
        {
            _databasePath = databasePath;
            Factory = factory;
            Session = session;
            AdminId = adminId;
            EmployeeId = employeeId;
            SupplierId = supplierId;
            ProductId = productId;
            AccountId = accountId;
        }

        public FileContextFactory Factory { get; }
        public CurrentSession Session { get; }
        public int AdminId { get; }
        public int EmployeeId { get; }
        public int SupplierId { get; }
        public int ProductId { get; }
        public int AccountId { get; }

        public static async Task<FactoryFixture> CreateAsync()
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"arte-global-persistence-{Guid.NewGuid():N}.db");
            var factory = new FileContextFactory(databasePath);

            await using var context = factory.CreateDbContext();
            await context.Database.EnsureCreatedAsync();

            var admin = NewUser("Admin global", "admin-global", RolUsuario.ADMIN);
            var employee = NewUser("Empleado global", "employee-global", RolUsuario.EMPLEADO);
            var category = new Categoria
            {
                Nombre = "Categoria global",
                Activa = true,
                FechaCreacion = DateTime.UtcNow
            };
            context.Usuarios.AddRange(admin, employee);
            context.Categorias.Add(category);
            await context.SaveChangesAsync();

            var supplier = new Proveedor
            {
                CodigoProveedor = "PRV-GLOBAL",
                Tipo = TipoProveedor.PROVEEDOR,
                NombreRazonSocial = "Proveedor global",
                NombreNormalizado = "proveedor global",
                Estado = EstadoProveedor.ACTIVO,
                FechaCreacion = DateTime.UtcNow,
                FechaActualizacion = DateTime.UtcNow,
                UsuarioCreacionId = admin.UsuarioId
            };
            var product = new Producto
            {
                CodigoInterno = "PRD-GLOBAL",
                Nombre = "Producto global",
                CategoriaId = category.CategoriaId,
                PrecioCompraCentavos = 100,
                PrecioVentaCentavos = 2000,
                StockActual = 100,
                StockMinimo = 1,
                Estado = EstadoProducto.ACTIVO,
                FechaCreacion = DateTime.UtcNow
            };
            var customer = new Cliente
            {
                CodigoCliente = "CLI-GLOBAL",
                NombreCompleto = "Cliente global",
                NombreNormalizado = "cliente global",
                Estado = EstadoCliente.ACTIVO,
                CreditoHabilitado = true,
                UsuarioCreacionId = admin.UsuarioId,
                FechaCreacion = DateTime.UtcNow,
                FechaActualizacion = DateTime.UtcNow
            };
            var cash = new Caja
            {
                FechaOperacion = DateOnly.FromDateTime(DateTime.Today),
                FechaApertura = DateTime.UtcNow,
                MontoInicialCentavos = 0,
                MontoCierreCentavos = 0,
                Estado = EstadoCaja.ABIERTA,
                UsuarioAperturaId = admin.UsuarioId
            };
            context.Proveedores.Add(supplier);
            context.Productos.Add(product);
            context.Clientes.Add(customer);
            context.Cajas.Add(cash);
            await context.SaveChangesAsync();

            var sale = new Venta
            {
                NumeroVenta = "VTA-GLOBAL-CREDIT",
                CajaId = cash.CajaId,
                UsuarioId = admin.UsuarioId,
                ClienteId = customer.ClienteId,
                ClienteNombreSnapshot = customer.NombreCompleto,
                FechaVenta = DateTime.UtcNow,
                SubtotalCentavos = 2000,
                TotalCentavos = 2000,
                PagadoCentavos = 0,
                CambioCentavos = 0,
                Estado = EstadoVenta.COMPLETADA
            };
            context.Ventas.Add(sale);
            await context.SaveChangesAsync();

            var saleLine = new DetalleVenta
            {
                VentaId = sale.VentaId,
                ProductoId = product.ProductoId,
                Cantidad = 1,
                PrecioUnitarioCentavos = 2000,
                SubtotalCentavos = 2000,
                DescuentoCentavos = 0,
                TotalCentavos = 2000
            };
            context.DetalleVentas.Add(saleLine);
            await context.SaveChangesAsync();

            var account = new CuentaPorCobrar
            {
                CodigoCuenta = "CXC-GLOBAL",
                VentaId = sale.VentaId,
                ClienteId = customer.ClienteId,
                CajaId = cash.CajaId,
                UsuarioRegistroId = admin.UsuarioId,
                CapitalFinanciadoCentavos = 2000,
                InteresPorcentaje = 0,
                InteresCalculadoCentavos = 0,
                TotalOriginalCentavos = 2000,
                TotalPagadoCentavos = 0,
                SaldoActualCentavos = 2000,
                FechaCreacion = DateTime.UtcNow,
                FechaVencimiento = DateTime.UtcNow.AddDays(30),
                Estado = EstadoCuentaPorCobrar.ABIERTA,
                FechaActualizacion = DateTime.UtcNow
            };
            context.CuentasPorCobrar.Add(account);
            await context.SaveChangesAsync();

            context.CuentasPorCobrarDetalle.Add(new CuentaPorCobrarDetalle
            {
                CuentaPorCobrarId = account.CuentaPorCobrarId,
                DetalleVentaId = saleLine.DetalleVentaId,
                ProductoId = product.ProductoId,
                ProductoCodigoSnapshot = product.CodigoInterno,
                ProductoNombreSnapshot = product.Nombre,
                CantidadFinanciada = 1,
                PrecioUnitarioCentavos = 2000,
                SubtotalFinanciadoCentavos = 2000,
                CapitalPendienteCentavos = 2000
            });
            await context.SaveChangesAsync();

            var session = new CurrentSession();
            session.IniciarSesion(new UsuarioSesion(
                admin.UsuarioId,
                admin.NombreCompleto,
                admin.NombreUsuario,
                admin.Rol));

            return new FactoryFixture(
                databasePath,
                factory,
                session,
                admin.UsuarioId,
                employee.UsuarioId,
                supplier.ProveedorId,
                product.ProductoId,
                account.CuentaPorCobrarId);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var context = Factory.CreateDbContext();
                await context.Database.EnsureDeletedAsync();
            }
            finally
            {
                if (File.Exists(_databasePath))
                {
                    File.Delete(_databasePath);
                }
            }
        }

        private static Usuario NewUser(string fullName, string userName, RolUsuario role) => new()
        {
            NombreCompleto = fullName,
            NombreUsuario = userName,
            PasswordHash = "test-hash",
            Rol = role,
            Activo = true,
            FechaCreacion = DateTime.UtcNow
        };
    }

    private sealed class FileContextFactory(string databasePath) : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options =
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={databasePath};Default Timeout=5")
                .Options;

        public AppDbContext CreateDbContext() => new(_options);
    }
}
