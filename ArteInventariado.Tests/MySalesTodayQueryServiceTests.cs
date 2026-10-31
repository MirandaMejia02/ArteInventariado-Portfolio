using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public sealed class MySalesTodayQueryServiceTests
{
    [Fact]
    public void PermissionIsAssignableAndDisabledByDefault()
    {
        Assert.Equal(9, (int)PermisoEmpleado.VENTAS_PROPIAS_DIA_VER);
        Assert.Equal(
            Enum.GetValues<PermisoEmpleado>().Length,
            Enum.GetValues<PermisoEmpleado>().Distinct().Count());

        var state = EmployeePermissionCatalog.BuildTemplateState(new HashSet<PermisoEmpleado>());
        Assert.False(state[PermisoEmpleado.VENTAS_PROPIAS_DIA_VER]);
        Assert.DoesNotContain(
            PermisoEmpleado.VENTAS_PROPIAS_DIA_VER,
            EmployeePermissionCatalog.RecommendedExistingEmployeePermissions);
        Assert.DoesNotContain(
            PermisoEmpleado.VENTAS_PROPIAS_DIA_VER,
            EmployeePermissionCatalog.SafeOperationalPermissions);
        Assert.False(EmployeePermissionCatalog.Dependencies.ContainsKey(PermisoEmpleado.VENTAS_PROPIAS_DIA_VER));

        var enabled = EmployeePermissionCatalog.BuildTemplateState(
            new HashSet<PermisoEmpleado> { PermisoEmpleado.VENTAS_PROPIAS_DIA_VER });
        Assert.Single(enabled, pair => pair.Value);
    }

    [Fact]
    public void PermissionAppearsInCatalogAndDashboardMenu()
    {
        var definition = EmployeePermissionCatalog.GetDefinition(PermisoEmpleado.VENTAS_PROPIAS_DIA_VER);
        Assert.Equal("Historiales", definition.Grupo);
        Assert.Equal("Ver mis ventas de hoy", definition.Nombre);
        Assert.True(EmployeePermissionCatalog.IsAssignable(PermisoEmpleado.VENTAS_PROPIAS_DIA_VER));

        var states = EmployeePermissionCatalog.AllPermissions.ToDictionary(permission => permission, _ => false);
        states[PermisoEmpleado.VENTAS_PROPIAS_DIA_VER] = true;
        var menu = EmployeeDashboardMoreMenuCatalog.Build(states);

        var item = Assert.Single(menu);
        Assert.Equal(EmployeeDashboardMoreAction.MisVentasHoy, item.Action);
        Assert.Equal("Mis ventas de hoy", item.Text);
        Assert.Equal(PermisoEmpleado.VENTAS_PROPIAS_DIA_VER, item.RequiredPermission);
    }

    [Fact]
    public async Task EmployeeWithoutPermissionCannotQuery()
    {
        await using var fixture = await MySalesFixture.CreateAsync(grantPermission: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.GetSalesAsync());
    }

    [Fact]
    public async Task EmployeeWithPermissionSeesOnlyOwnSales()
    {
        await using var fixture = await MySalesFixture.CreateAsync(grantPermission: true);

        var sales = await fixture.Service.GetSalesAsync();

        Assert.Contains(sales, sale => sale.NumeroVenta == "A-HOY");
        Assert.Contains(sales, sale => sale.NumeroVenta == "A-INICIO");
        Assert.DoesNotContain(sales, sale => sale.NumeroVenta == "B-HOY");
        Assert.Equal(2, sales.Count);
    }

    [Fact]
    public async Task YesterdayAndTomorrowAreExcluded()
    {
        await using var fixture = await MySalesFixture.CreateAsync(grantPermission: true);

        var sales = await fixture.Service.GetSalesAsync();

        Assert.DoesNotContain(sales, sale => sale.NumeroVenta == "A-AYER");
        Assert.DoesNotContain(sales, sale => sale.NumeroVenta == "A-MANIANA");
    }

    [Fact]
    public async Task DetailRechecksOwnUserAndCurrentDay()
    {
        await using var fixture = await MySalesFixture.CreateAsync(grantPermission: true);

        var own = await fixture.Service.GetSaleDetailAsync(fixture.OwnTodaySaleId);
        var otherUser = await fixture.Service.GetSaleDetailAsync(fixture.OtherUserSaleId);
        var yesterday = await fixture.Service.GetSaleDetailAsync(fixture.YesterdaySaleId);

        Assert.NotNull(own);
        Assert.Null(otherUser);
        Assert.Null(yesterday);
    }

    [Fact]
    public async Task SafeModelsExposeNoCostMarginOrCreditInternals()
    {
        await using var fixture = await MySalesFixture.CreateAsync(grantPermission: true);
        var detail = await fixture.Service.GetSaleDetailAsync(fixture.OwnTodaySaleId);

        Assert.NotNull(detail);
        var modelTypes = new[]
        {
            typeof(MySalesTodaySummary),
            typeof(MySalesTodayDetail),
            typeof(MySalesTodayLine),
            typeof(MySalesTodayPayment)
        };
        foreach (var property in modelTypes.SelectMany(type => type.GetProperties()))
        {
            Assert.DoesNotContain("cost", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("margen", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("utilidad", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("capital", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("interes", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("saldo", property.Name, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(detail!.Payments, payment => payment.Metodo == "CREDITO" && payment.MontoCentavos != 0);
    }

    [Fact]
    public void BusinessDayBoundsAreHalfOpenAndTimezoneAware()
    {
        var businessZone = TimeZoneInfo.CreateCustomTimeZone(
            "BusinessMinusSix",
            TimeSpan.FromHours(-6),
            "Business -06",
            "Business -06");
        var bounds = MySalesTodayQueryService.CalculateUtcDayBounds(
            new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero),
            businessZone);

        Assert.Equal(new DateTime(2026, 8, 30, 6, 0, 0, DateTimeKind.Utc), bounds.StartUtc);
        Assert.Equal(new DateTime(2026, 8, 31, 6, 0, 0, DateTimeKind.Utc), bounds.EndUtcExclusive);
        Assert.True(bounds.StartUtc < bounds.EndUtcExclusive);
    }

    [Fact]
    public async Task AdminRetainsGlobalAccess()
    {
        await using var fixture = await MySalesFixture.CreateAsync(grantPermission: false);
        fixture.SignInAdmin();

        var sales = await fixture.Service.GetSalesAsync();

        Assert.Contains(sales, sale => sale.NumeroVenta == "ADMIN-HOY");
    }

    [Fact]
    public async Task RemovingPermissionHidesModuleAndBlocksService()
    {
        await using var fixture = await MySalesFixture.CreateAsync(grantPermission: true);
        var states = EmployeePermissionCatalog.AllPermissions.ToDictionary(permission => permission, _ => false);
        states[PermisoEmpleado.VENTAS_PROPIAS_DIA_VER] = true;
        var before = EmployeeDashboardMoreMenuCatalog.Build(states);
        Assert.Single(before);

        fixture.SetOwnPermission(false);
        states[PermisoEmpleado.VENTAS_PROPIAS_DIA_VER] = false;
        var after = EmployeeDashboardMoreMenuCatalog.Build(states);
        Assert.Empty(after);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.GetSalesAsync());
    }

    [Fact]
    public async Task QueryIsReadOnlyAndWorksWithSessionScope()
    {
        await using var fixture = await MySalesFixture.CreateAsync(grantPermission: true);
        var before = fixture.Context.ChangeTracker.Entries().Count();

        _ = await fixture.Service.GetSalesAsync();

        Assert.Equal(before, fixture.Context.ChangeTracker.Entries().Count());
        Assert.All(fixture.Context.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
    }

    private sealed class MySalesFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly TimeProvider _timeProvider;
        private readonly AuditService _auditService;
        private readonly Usuario _admin;
        private readonly Usuario _employee;

        private MySalesFixture(
            SqliteConnection connection,
            AppDbContext context,
            CurrentSession session,
            EmployeePermissionService permissionService,
            MySalesTodayQueryService service,
            TimeProvider timeProvider,
            AuditService auditService,
            Usuario admin,
            Usuario employee,
            int ownTodaySaleId,
            int otherUserSaleId,
            int yesterdaySaleId)
        {
            _connection = connection;
            Context = context;
            Session = session;
            PermissionService = permissionService;
            Service = service;
            _timeProvider = timeProvider;
            _auditService = auditService;
            _admin = admin;
            _employee = employee;
            OwnTodaySaleId = ownTodaySaleId;
            OtherUserSaleId = otherUserSaleId;
            YesterdaySaleId = yesterdaySaleId;
        }

        public AppDbContext Context { get; }
        public CurrentSession Session { get; }
        public EmployeePermissionService PermissionService { get; }
        public MySalesTodayQueryService Service { get; }
        public int OwnTodaySaleId { get; }
        public int OtherUserSaleId { get; }
        public int YesterdaySaleId { get; }

        public static async Task<MySalesFixture> CreateAsync(bool grantPermission)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var admin = new Usuario
            {
                NombreCompleto = "Admin",
                NombreUsuario = $"admin-{Guid.NewGuid():N}",
                PasswordHash = "hash",
                Rol = RolUsuario.ADMIN,
                Activo = true
            };
            var employee = new Usuario
            {
                NombreCompleto = "Empleado A",
                NombreUsuario = $"employee-{Guid.NewGuid():N}",
                PasswordHash = "hash",
                Rol = RolUsuario.EMPLEADO,
                Activo = true
            };
            var other = new Usuario
            {
                NombreCompleto = "Empleado B",
                NombreUsuario = $"other-{Guid.NewGuid():N}",
                PasswordHash = "hash",
                Rol = RolUsuario.EMPLEADO,
                Activo = true
            };
            var category = new Categoria { Nombre = $"Categoria-{Guid.NewGuid():N}", Activa = true };
            context.Usuarios.AddRange(admin, employee, other);
            context.Categorias.Add(category);
            await context.SaveChangesAsync();

            var product = new Producto
            {
                CodigoInterno = $"PROD-{Guid.NewGuid():N}",
                Nombre = "Producto de prueba",
                CategoriaId = category.CategoriaId,
                PrecioCompraCentavos = 500,
                PrecioVentaCentavos = 1000,
                StockActual = 10,
                StockMinimo = 1,
                Estado = EstadoProducto.ACTIVO
            };
            context.Productos.Add(product);
            context.Cajas.Add(new Caja
            {
                FechaOperacion = new DateOnly(2026, 8, 30),
                FechaApertura = new DateTime(2026, 8, 30, 7, 0, 0, DateTimeKind.Utc),
                MontoInicialCentavos = 0,
                Estado = EstadoCaja.ABIERTA,
                UsuarioAperturaId = admin.UsuarioId
            });
            context.ConfiguracionSistema.Add(new ConfiguracionSistema
            {
                Clave = DashboardQueryService.BusinessTimeZoneKey,
                Valor = "UTC"
            });
            await context.SaveChangesAsync();

            context.UsuarioPermisos.Add(new UsuarioPermiso
            {
                UsuarioId = employee.UsuarioId,
                Permiso = PermisoEmpleado.VENTAS_PROPIAS_DIA_VER,
                Habilitado = grantPermission
            });

            var now = new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);
            var ownToday = AddSale(context, employee.UsuarioId, 1, product.ProductoId, "A-HOY", now.AddHours(-1));
            AddSale(context, employee.UsuarioId, 1, product.ProductoId, "A-INICIO", now.Date);
            var yesterday = AddSale(context, employee.UsuarioId, 1, product.ProductoId, "A-AYER", now.Date.AddTicks(-1));
            AddSale(context, employee.UsuarioId, 1, product.ProductoId, "A-MANIANA", now.Date.AddDays(1));
            var otherUser = AddSale(context, other.UsuarioId, 1, product.ProductoId, "B-HOY", now.AddHours(-2));
            AddSale(context, admin.UsuarioId, 1, product.ProductoId, "ADMIN-HOY", now.AddHours(-3));
            await context.SaveChangesAsync();

            var session = new CurrentSession();
            session.IniciarSesion(new UsuarioSesion(
                employee.UsuarioId,
                employee.NombreCompleto,
                employee.NombreUsuario,
                employee.Rol));
            var audit = new AuditService(context);
            var permissionService = new EmployeePermissionService(context, session, audit);
            var timeProvider = new FixedTimeProvider(new DateTimeOffset(now, TimeSpan.Zero));
            var service = new MySalesTodayQueryService(context, session, permissionService, timeProvider);
            return new MySalesFixture(
                connection,
                context,
                session,
                permissionService,
                service,
                timeProvider,
                audit,
                admin,
                employee,
                ownToday.VentaId,
                otherUser.VentaId,
                yesterday.VentaId);
        }

        public void SignInAdmin()
        {
            Session.IniciarSesion(new UsuarioSesion(
                _admin.UsuarioId,
                _admin.NombreCompleto,
                _admin.NombreUsuario,
                _admin.Rol));
        }

        public void SetOwnPermission(bool enabled)
        {
            var permission = Context.UsuarioPermisos.Single(item =>
                item.UsuarioId == _employee.UsuarioId &&
                item.Permiso == PermisoEmpleado.VENTAS_PROPIAS_DIA_VER);
            permission.Habilitado = enabled;
            Context.SaveChanges();
            Session.IniciarSesion(new UsuarioSesion(
                _employee.UsuarioId,
                _employee.NombreCompleto,
                _employee.NombreUsuario,
                _employee.Rol));
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private static Venta AddSale(
            AppDbContext context,
            int userId,
            int cajaId,
            int productId,
            string number,
            DateTime date)
        {
            var sale = new Venta
            {
                NumeroVenta = number,
                CajaId = cajaId,
                UsuarioId = userId,
                FechaVenta = date,
                SubtotalCentavos = 1000,
                TotalCentavos = 1000,
                PagadoCentavos = 1000,
                Estado = EstadoVenta.COMPLETADA,
                Detalles =
                {
                    new DetalleVenta
                    {
                        ProductoId = productId,
                        Cantidad = 1,
                        PrecioUnitarioCentavos = 1000,
                        SubtotalCentavos = 1000,
                        TotalCentavos = 1000,
                        ProductoNombreSnapshot = "Producto de prueba",
                        ProductoCodigoSnapshot = "PROD-TEST",
                        CodigoSkuSnapshot = "SKU-TEST",
                        CostoUnitarioCentavos = null,
                        OrigenCosto = OrigenCostoHistorico.DESCONOCIDO
                    }
                },
                Pagos =
                {
                    new PagoVenta
                    {
                        MetodoPago = MetodoPago.EFECTIVO,
                        MontoCentavos = 1000,
                        FechaPago = date
                    }
                }
            };
            context.Ventas.Add(sale);
            return sale;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
