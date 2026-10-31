using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ArteInventariado.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Tests;

public class EmployeePermissionServiceTests
{
    [Fact]
    public async Task AdminCanUpdateEmployeePermissions()
    {
        await using var fixture = await PermissionFixture.CreateAsync();
        fixture.SignInAdmin();

        await fixture.SetPermissionAsync(PermisoEmpleado.POS_ACCEDER, false);

        Assert.False(await fixture.GetStoredPermissionAsync(fixture.EmployeeId, PermisoEmpleado.POS_ACCEDER));
    }

    [Fact]
    public async Task EmployeeWithPermissionCanAccessAction()
    {
        await using var fixture = await PermissionFixture.CreateAsync();
        fixture.SignInEmployee();

        Assert.True(await fixture.PermissionService.HasPermissionAsync(PermisoEmpleado.POS_ACCEDER));
    }

    [Fact]
    public async Task EmployeeWithoutPermissionIsRejectedByService()
    {
        await using var fixture = await PermissionFixture.CreateAsync();
        await fixture.SetPermissionAsync(PermisoEmpleado.POS_ACCEDER, false);
        fixture.SignInEmployee();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.PermissionService.EnsurePermissionAsync(PermisoEmpleado.POS_ACCEDER));

        Assert.Contains("No tiene permiso", ex.Message);
    }

    [Fact]
    public async Task DashboardVisibilitySourceReturnsFalseWhenPermissionIsDisabled()
    {
        await using var fixture = await PermissionFixture.CreateAsync();
        await fixture.SetPermissionAsync(PermisoEmpleado.CORRECCIONES_ACCEDER, false);
        fixture.SignInEmployee();

        var visible = await fixture.PermissionService.HasPermissionAsync(PermisoEmpleado.CORRECCIONES_ACCEDER);

        Assert.False(visible);
    }

    [Fact]
    public async Task IndirectSaleAccessIsBlockedWhenPosPermissionIsRevoked()
    {
        await using var fixture = await PermissionFixture.CreateAsync();
        await fixture.SetPermissionAsync(PermisoEmpleado.POS_ACCEDER, false);
        fixture.SignInEmployee();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.SaleService.RegisterSaleAsync(new RegisterSaleRequest(
                [new SaleLineRequest(fixture.ProductId, 1)],
                [new SalePaymentRequest(MetodoPago.EFECTIVO, 1000)])));

        Assert.Contains("No tiene permiso", ex.Message);
        Assert.Equal(0, await fixture.Context.Ventas.CountAsync());
    }

    [Fact]
    public async Task ExistingEmployeeWithoutRowsReceivesRecommendedTemplate()
    {
        await using var fixture = await PermissionFixture.CreateAsync(seedEmployeePermissions: false);
        fixture.SignInEmployee();

        Assert.True(await fixture.PermissionService.HasPermissionAsync(PermisoEmpleado.POS_ACCEDER));
        Assert.True(await fixture.PermissionService.HasPermissionAsync(PermisoEmpleado.CAMBIO_REGISTRAR));
        Assert.False(await fixture.PermissionService.HasPermissionAsync(PermisoEmpleado.CLIENTES_ACCEDER));
    }

    [Fact]
    public async Task NewEmployeeReceivesRecommendedTemplate()
    {
        await using var fixture = await PermissionFixture.CreateAsync();
        fixture.SignInAdmin();
        var userService = new UserService(fixture.Context, fixture.AuditService);
        var employeePassword = TestCredentials.Password("employee");

        var user = await userService.CreateAsync(
            new CreateUserRequest("Nuevo empleado", $"nuevo-{Guid.NewGuid():N}", employeePassword, RolUsuario.EMPLEADO),
            fixture.AdminId);

        Assert.True(await fixture.GetStoredPermissionAsync(user.UsuarioId, PermisoEmpleado.POS_ACCEDER));
        Assert.True(await fixture.GetStoredPermissionAsync(user.UsuarioId, PermisoEmpleado.PAGO_BITCOIN_USAR));
        Assert.False(await fixture.GetStoredPermissionAsync(user.UsuarioId, PermisoEmpleado.CLIENTES_ACCEDER));
    }

    [Fact]
    public async Task InvalidPermissionCannotBeAssigned()
    {
        await using var fixture = await PermissionFixture.CreateAsync();
        fixture.SignInAdmin();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.PermissionService.UpdatePermissionsAsync(new UpdateEmployeePermissionsRequest(
                fixture.EmployeeId,
                [new EmployeePermissionUpdate((PermisoEmpleado)999, true)])));
    }

    [Fact]
    public async Task PermissionChangeIsAudited()
    {
        await using var fixture = await PermissionFixture.CreateAsync();
        fixture.SignInAdmin();

        await fixture.SetPermissionAsync(PermisoEmpleado.PAGO_BITCOIN_USAR, false);

        var audit = await fixture.Context.Bitacora
            .AsNoTracking()
            .SingleAsync(b => b.TipoAccion == TipoBitacora.PERMISOS_MODIFICADOS);
        Assert.Contains("PAGO_BITCOIN_USAR", audit.Descripcion);
        Assert.Equal(fixture.AdminId, audit.UsuarioId);
        Assert.Equal(fixture.EmployeeId, audit.EntidadId);
    }

    [Fact]
    public async Task RestoreRecommendedTemplateReenablesRecommendedPermissions()
    {
        await using var fixture = await PermissionFixture.CreateAsync();
        await fixture.SetPermissionAsync(PermisoEmpleado.POS_ACCEDER, false);
        fixture.SignInAdmin();

        await fixture.PermissionService.RestoreRecommendedTemplateAsync(fixture.EmployeeId);

        Assert.True(await fixture.GetStoredPermissionAsync(fixture.EmployeeId, PermisoEmpleado.POS_ACCEDER));
        Assert.True(await fixture.GetStoredPermissionAsync(fixture.EmployeeId, PermisoEmpleado.CAJA_ABRIR));
        Assert.False(await fixture.GetStoredPermissionAsync(fixture.EmployeeId, PermisoEmpleado.CLIENTES_ACCEDER));
    }

    [Fact]
    public async Task InactiveUserCannotOperateEvenWithPermission()
    {
        await using var fixture = await PermissionFixture.CreateAsync();
        fixture.SignInInactiveEmployee();

        Assert.False(await fixture.PermissionService.HasPermissionAsync(PermisoEmpleado.POS_ACCEDER));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.PermissionService.EnsurePermissionAsync(PermisoEmpleado.POS_ACCEDER));
    }

    [Fact]
    public async Task RevokingReturnsBlocksReturnRegistration()
    {
        await using var fixture = await PermissionFixture.CreateAsync();
        fixture.SignInEmployee();
        var sale = await fixture.CreateSaleAsync();

        await fixture.SetPermissionAsync(PermisoEmpleado.DEVOLUCION_REGISTRAR, false);
        fixture.SignInEmployee();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.ReturnService.RegisterReturnAsync(new RegisterReturnRequest(
                sale.VentaId,
                "Permiso revocado",
                [new RegisterReturnLineRequest(sale.Lines.Single().DetalleVentaId, 1, CondicionProductoDevuelto.BUENO, true)])));

        Assert.Contains("No tiene permiso", ex.Message);
        Assert.Equal(0, await fixture.Context.Devoluciones.CountAsync());
    }

    [Fact]
    public async Task SafeTemplateDoesNotEnableAdministrativeOrFutureFeatures()
    {
        await using var fixture = await PermissionFixture.CreateAsync();
        fixture.SignInAdmin();

        await fixture.PermissionService.ApplySafeOperationalTemplateAsync(fixture.EmployeeId);

        Assert.True(await fixture.GetStoredPermissionAsync(fixture.EmployeeId, PermisoEmpleado.POS_ACCEDER));
        Assert.True(await fixture.GetStoredPermissionAsync(fixture.EmployeeId, PermisoEmpleado.CAMBIO_REGISTRAR));
        Assert.False(await fixture.GetStoredPermissionAsync(fixture.EmployeeId, PermisoEmpleado.CAJA_CERRAR));
        Assert.False(await fixture.GetStoredPermissionAsync(fixture.EmployeeId, PermisoEmpleado.CLIENTES_ACCEDER));
    }

    [Fact]
    public async Task PartialPermissionUpdateIsRejected()
    {
        await using var fixture = await PermissionFixture.CreateAsync();
        fixture.SignInAdmin();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.PermissionService.UpdatePermissionsAsync(new UpdateEmployeePermissionsRequest(
                fixture.EmployeeId,
                [new EmployeePermissionUpdate(PermisoEmpleado.CAJA_ABRIR, true)])));

        Assert.Contains("todos los permisos", ex.Message);
    }

    [Fact]
    public async Task InvalidDependencyCombinationIsRejectedWithoutPersisting()
    {
        await using var fixture = await PermissionFixture.CreateAsync();
        fixture.SignInAdmin();
        var states = await fixture.GetStoredPermissionStatesAsync(fixture.EmployeeId);
        states[PermisoEmpleado.POS_ACCEDER] = false;
        states[PermisoEmpleado.VENTA_REGISTRAR] = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.PermissionService.UpdatePermissionsAsync(new UpdateEmployeePermissionsRequest(
                fixture.EmployeeId,
                fixture.ToUpdates(states))));

        Assert.True(await fixture.GetStoredPermissionAsync(fixture.EmployeeId, PermisoEmpleado.POS_ACCEDER));
    }

    [Fact]
    public async Task ReprintReceiptRequiresSalesHistoryPermission()
    {
        await using var fixture = await PermissionFixture.CreateAsync();
        fixture.SignInAdmin();
        var states = EmployeePermissionCatalog.AllPermissions.ToDictionary(permission => permission, _ => false);
        states[PermisoEmpleado.COMPROBANTE_REIMPRIMIR] = true;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.PermissionService.UpdatePermissionsAsync(new UpdateEmployeePermissionsRequest(
                fixture.EmployeeId,
                fixture.ToUpdates(states))));

        Assert.Contains("COMPROBANTE_REIMPRIMIR requiere HISTORIAL_VENTAS_VER", exception.Message);
        Assert.False(await fixture.GetStoredPermissionAsync(fixture.EmployeeId, PermisoEmpleado.COMPROBANTE_REIMPRIMIR));
    }

    [Fact]
    public void PermissionCatalogDeclaresReprintReceiptDependency()
    {
        Assert.True(EmployeePermissionCatalog.Dependencies.TryGetValue(
            PermisoEmpleado.COMPROBANTE_REIMPRIMIR,
            out var dependencies));
        Assert.Contains(PermisoEmpleado.HISTORIAL_VENTAS_VER, dependencies);
    }

    [Fact]
    public async Task EmployeeWithSalesHistoryCanQuerySalesWithoutReprintPermission()
    {
        await using var fixture = await PermissionFixture.CreateAsync();
        fixture.SignInAdmin();
        var sale = await fixture.CreateSaleAsync();
        await fixture.SetExactPermissionsAsync(PermisoEmpleado.HISTORIAL_VENTAS_VER);
        fixture.SignInEmployee();
        var queryService = new SalesQueryService(
            fixture.Context,
            fixture.CurrentSession,
            fixture.PermissionService);

        var sales = await queryService.GetSalesAsync(new SalesHistoryFilter(NumeroVenta: sale.NumeroVenta));
        var detail = await queryService.GetSaleDetailAsync(sale.VentaId);

        Assert.Contains(sales, s => s.VentaId == sale.VentaId);
        Assert.NotNull(detail);
        Assert.False(await fixture.PermissionService.HasPermissionAsync(PermisoEmpleado.COMPROBANTE_REIMPRIMIR));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.PermissionService.EnsurePermissionAsync(PermisoEmpleado.COMPROBANTE_REIMPRIMIR));
    }

    [Fact]
    public async Task EmployeeWithoutSalesHistoryCannotQuerySales()
    {
        await using var fixture = await PermissionFixture.CreateAsync();
        fixture.SignInAdmin();
        await fixture.CreateSaleAsync();
        await fixture.SetExactPermissionsAsync();
        fixture.SignInEmployee();
        var queryService = new SalesQueryService(
            fixture.Context,
            fixture.CurrentSession,
            fixture.PermissionService);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            queryService.GetSalesAsync(new SalesHistoryFilter()));
    }

    [Fact]
    public async Task EmployeeWithReturnsHistoryCanQueryReturnsWithoutCorrectionAccess()
    {
        await using var fixture = await PermissionFixture.CreateAsync();
        fixture.SignInAdmin();
        var sale = await fixture.CreateSaleAsync();
        var returnResult = await fixture.ReturnService.RegisterReturnAsync(new RegisterReturnRequest(
            sale.VentaId,
            "Consulta de historial",
            [new RegisterReturnLineRequest(
                sale.Lines.Single().DetalleVentaId,
                1,
                CondicionProductoDevuelto.BUENO,
                true)]));
        await fixture.SetExactPermissionsAsync(PermisoEmpleado.HISTORIAL_DEVOLUCIONES_VER);
        fixture.SignInEmployee();
        var queryService = new SaleCorrectionQueryService(
            fixture.Context,
            fixture.CurrentSession,
            fixture.PermissionService);

        var returns = await queryService.GetReturnHistoryAsync(new ReturnHistoryFilter(
            NumeroVenta: sale.NumeroVenta));
        var detail = await queryService.GetReturnDetailAsync(returnResult.DevolucionId);

        Assert.Contains(returns, r => r.DevolucionId == returnResult.DevolucionId);
        Assert.NotNull(detail);
        Assert.False(await fixture.PermissionService.HasPermissionAsync(PermisoEmpleado.CORRECCIONES_ACCEDER));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            queryService.SearchSalesForCorrectionAsync(new SaleCorrectionSalesFilter()));
    }

    [Fact]
    public async Task EmployeeWithoutReturnsHistoryCannotQueryReturns()
    {
        await using var fixture = await PermissionFixture.CreateAsync();
        await fixture.SetExactPermissionsAsync();
        fixture.SignInEmployee();
        var queryService = new SaleCorrectionQueryService(
            fixture.Context,
            fixture.CurrentSession,
            fixture.PermissionService);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            queryService.GetReturnHistoryAsync(new ReturnHistoryFilter()));
    }

    [Fact]
    public async Task CajaAbrirPersistsWhenOnlyCashOpenPermissionIsEnabled()
    {
        await using var fixture = await PermissionFixture.CreateAsync();
        fixture.SignInAdmin();
        var states = EmployeePermissionCatalog.AllPermissions.ToDictionary(permission => permission, _ => false);
        states[PermisoEmpleado.CAJA_ABRIR] = true;

        await fixture.PermissionService.UpdatePermissionsAsync(new UpdateEmployeePermissionsRequest(
            fixture.EmployeeId,
            fixture.ToUpdates(states)));

        var persisted = await fixture.GetStoredPermissionStatesFromNewContextAsync(fixture.EmployeeId);
        Assert.True(persisted[PermisoEmpleado.CAJA_ABRIR]);
        Assert.False(persisted[PermisoEmpleado.CAJA_CERRAR]);
        Assert.False(persisted[PermisoEmpleado.GASTO_REGISTRAR]);
        Assert.False(persisted[PermisoEmpleado.RETIRO_REGISTRAR]);
        Assert.False(persisted[PermisoEmpleado.CAJA_TOTAL_ESPERADO_VER]);
        Assert.False(persisted[PermisoEmpleado.CAJA_DIFERENCIAS_VER]);

        var loaded = await fixture.PermissionService.GetPermissionsForUserAsync(fixture.EmployeeId);
        Assert.True(loaded.Single(p => p.Permiso == PermisoEmpleado.CAJA_ABRIR).Habilitado);
    }

    [Fact]
    public async Task EmployeeWithOnlyCajaAbrirCanOpenCashAndOtherCashActionsStayBlocked()
    {
        await using var fixture = await PermissionFixture.CreateAsync(seedOpenCash: false);
        fixture.SignInAdmin();
        var states = EmployeePermissionCatalog.AllPermissions.ToDictionary(permission => permission, _ => false);
        states[PermisoEmpleado.CAJA_ABRIR] = true;

        await fixture.PermissionService.UpdatePermissionsAsync(new UpdateEmployeePermissionsRequest(
            fixture.EmployeeId,
            fixture.ToUpdates(states)));

        fixture.SignInEmployee();
        var summary = await fixture.CashService.OpenCashAsync(new OpenCashRequest(0, "Prueba CAJA_ABRIR"));

        Assert.NotNull(summary.CajaId);
        Assert.Equal(EstadoCaja.ABIERTA, summary.Estado);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.CashService.CloseCashAsync(new CloseCashRequest(0, "Sin permiso")));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.CashService.RegisterExpenseAsync(new RegisterExpenseRequest(100, "Sin permiso")));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.CashService.RegisterWithdrawalAsync(new RegisterWithdrawalRequest(100, "Sin permiso")));
    }

    [Fact]
    public async Task SavingMultipleTimesDoesNotCreateDuplicatePermissionRows()
    {
        await using var fixture = await PermissionFixture.CreateAsync();
        fixture.SignInAdmin();
        var states = await fixture.GetStoredPermissionStatesAsync(fixture.EmployeeId);

        await fixture.PermissionService.UpdatePermissionsAsync(new UpdateEmployeePermissionsRequest(
            fixture.EmployeeId,
            fixture.ToUpdates(states)));
        await fixture.PermissionService.UpdatePermissionsAsync(new UpdateEmployeePermissionsRequest(
            fixture.EmployeeId,
            fixture.ToUpdates(states)));

        var totalRows = await fixture.Context.UsuarioPermisos
            .AsNoTracking()
            .CountAsync(p => p.UsuarioId == fixture.EmployeeId);
        var duplicateGroups = await fixture.Context.UsuarioPermisos
            .AsNoTracking()
            .Where(p => p.UsuarioId == fixture.EmployeeId)
            .GroupBy(p => p.Permiso)
            .Where(group => group.Count() > 1)
            .CountAsync();

        Assert.Equal(EmployeePermissionCatalog.AllPermissions.Count, totalRows);
        Assert.Equal(0, duplicateGroups);
    }

    [Fact]
    public async Task PermissionCatalogMappingAndTemplatesAreConsistent()
    {
        var enumPermissions = Enum.GetValues<PermisoEmpleado>().ToHashSet();
        var catalogPermissions = EmployeePermissionCatalog.All.Select(p => p.Permiso).ToList();

        Assert.Equal(catalogPermissions.Count, catalogPermissions.Distinct().Count());
        Assert.True(enumPermissions.SetEquals(catalogPermissions));
        Assert.All(EmployeePermissionCatalog.RecommendedNewEmployeePermissions, AssertAssignable);
        Assert.All(EmployeePermissionCatalog.SafeOperationalPermissions, AssertAssignable);
        Assert.All(EmployeePermissionCatalog.Dependencies.Keys, AssertAssignable);
        Assert.All(EmployeePermissionCatalog.Dependencies.Values.SelectMany(v => v), AssertAssignable);
        Assert.False(EmployeePermissionCatalog.RecommendedNewEmployeePermissions.Contains(
            PermisoEmpleado.PRODUCTOS_COSTO_COMPRA_EDITAR));
        Assert.Contains(
            PermisoEmpleado.PRODUCTOS_GESTIONAR,
            EmployeePermissionCatalog.Dependencies[PermisoEmpleado.PRODUCTOS_COSTO_COMPRA_EDITAR]);

        static void AssertAssignable(PermisoEmpleado permission)
        {
            Assert.True(EmployeePermissionCatalog.IsAssignable(permission), $"{permission} is not assignable.");
        }
    }

    [Fact]
    public void EnablingPurchasePaymentAlsoEnablesAccountsPayableAndPurchases()
    {
        var states = EmployeePermissionCatalog.AllPermissions
            .ToDictionary(permission => permission, _ => false);

        EmployeePermissionCatalog.EnableWithDependencies(
            states,
            PermisoEmpleado.PAGO_COMPRA_REGISTRAR);

        Assert.True(states[PermisoEmpleado.PAGO_COMPRA_REGISTRAR]);
        Assert.True(states[PermisoEmpleado.CUENTAS_PAGAR_ACCEDER]);
        Assert.True(states[PermisoEmpleado.COMPRAS_ACCEDER]);
        Assert.Empty(EmployeePermissionCatalog.ValidateStateCombination(states));
    }

    [Fact]
    public void DisablingAccountsPayableDisablesAllEnabledFinancialChildren()
    {
        var states = EmployeePermissionCatalog.AllPermissions
            .ToDictionary(permission => permission, _ => false);
        foreach (var permission in new[]
                 {
                     PermisoEmpleado.PAGO_COMPRA_REGISTRAR,
                     PermisoEmpleado.ABONO_PROVEEDOR_REGISTRAR,
                     PermisoEmpleado.PAGOS_PROVEEDOR_HISTORIAL_VER,
                     PermisoEmpleado.FINANZAS_PROVEEDOR_VER
                 })
        {
            EmployeePermissionCatalog.EnableWithDependencies(states, permission);
        }

        EmployeePermissionCatalog.DisableWithDependents(
            states,
            PermisoEmpleado.CUENTAS_PAGAR_ACCEDER);

        Assert.False(states[PermisoEmpleado.CUENTAS_PAGAR_ACCEDER]);
        Assert.False(states[PermisoEmpleado.PAGO_COMPRA_REGISTRAR]);
        Assert.False(states[PermisoEmpleado.ABONO_PROVEEDOR_REGISTRAR]);
        Assert.False(states[PermisoEmpleado.PAGOS_PROVEEDOR_HISTORIAL_VER]);
        Assert.False(states[PermisoEmpleado.FINANZAS_PROVEEDOR_VER]);
        Assert.Empty(EmployeePermissionCatalog.ValidateStateCombination(states));
    }

    [Fact]
    public void PermissionTemplatesAlwaysIncludeRequiredParents()
    {
        foreach (var template in new[]
                 {
                     EmployeePermissionCatalog.RecommendedNewEmployeePermissions,
                     EmployeePermissionCatalog.SafeOperationalPermissions
                 })
        {
            var states = EmployeePermissionCatalog.BuildTemplateState(template);
            Assert.Empty(EmployeePermissionCatalog.ValidateStateCombination(states));
        }
    }

    [Fact]
    public async Task PurchasePaymentWithoutAccountsPayableIsRejectedByService()
    {
        await using var fixture = await PermissionFixture.CreateAsync();
        fixture.SignInAdmin();
        var states = EmployeePermissionCatalog.AllPermissions
            .ToDictionary(permission => permission, _ => false);
        states[PermisoEmpleado.COMPRAS_ACCEDER] = true;
        states[PermisoEmpleado.PAGO_COMPRA_REGISTRAR] = true;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.PermissionService.UpdatePermissionsAsync(new UpdateEmployeePermissionsRequest(
                fixture.EmployeeId,
                fixture.ToUpdates(states))));

        Assert.Contains(nameof(PermisoEmpleado.CUENTAS_PAGAR_ACCEDER), exception.Message);
        Assert.False(await fixture.GetStoredPermissionAsync(
            fixture.EmployeeId,
            PermisoEmpleado.PAGO_COMPRA_REGISTRAR));
    }

    [Fact]
    public async Task EveryPermissionPersistsEnabledAndDisabledSequentially()
    {
        await using var fixture = await PermissionFixture.CreateAsync();
        fixture.SignInAdmin();
        var report = new List<string>();

        foreach (var permission in EmployeePermissionCatalog.AllPermissions.OrderBy(permission => permission))
        {
            var enabledStates = EmployeePermissionCatalog.AllPermissions.ToDictionary(p => p, _ => false);
            fixture.EnablePermissionWithDependencies(enabledStates, permission);

            await fixture.PermissionService.UpdatePermissionsAsync(new UpdateEmployeePermissionsRequest(
                fixture.EmployeeId,
                fixture.ToUpdates(enabledStates)));

            var enabledPersisted = await fixture.GetStoredPermissionStatesFromNewContextAsync(fixture.EmployeeId);
            Assert.True(enabledPersisted[permission]);

            var loaded = await fixture.PermissionService.GetPermissionsForUserAsync(fixture.EmployeeId);
            Assert.True(loaded.Single(p => p.Permiso == permission).Habilitado);

            var disabledStates = enabledStates.ToDictionary(pair => pair.Key, pair => pair.Value);
            fixture.DisablePermissionAndDependents(disabledStates, permission);

            await fixture.PermissionService.UpdatePermissionsAsync(new UpdateEmployeePermissionsRequest(
                fixture.EmployeeId,
                fixture.ToUpdates(disabledStates)));

            var disabledPersisted = await fixture.GetStoredPermissionStatesFromNewContextAsync(fixture.EmployeeId);
            Assert.False(disabledPersisted[permission]);

            report.Add($"{permission}: OK");
        }

        Assert.Equal(EmployeePermissionCatalog.AllPermissions.Count, report.Count);
    }

    private sealed class PermissionFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private PermissionFixture(
            SqliteConnection connection,
            DbContextOptions<AppDbContext> options,
            AppDbContext context,
            TestCurrentSession currentSession,
            AuditService auditService,
            EmployeePermissionService permissionService,
            SaleService saleService,
            ReturnService returnService,
            CashService cashService,
            int adminId,
            int employeeId,
            int inactiveEmployeeId,
            int productId)
        {
            _connection = connection;
            Options = options;
            Context = context;
            CurrentSession = currentSession;
            AuditService = auditService;
            PermissionService = permissionService;
            SaleService = saleService;
            ReturnService = returnService;
            CashService = cashService;
            AdminId = adminId;
            EmployeeId = employeeId;
            InactiveEmployeeId = inactiveEmployeeId;
            ProductId = productId;
        }

        public AppDbContext Context { get; }
        public DbContextOptions<AppDbContext> Options { get; }
        public TestCurrentSession CurrentSession { get; }
        public AuditService AuditService { get; }
        public EmployeePermissionService PermissionService { get; }
        public SaleService SaleService { get; }
        public ReturnService ReturnService { get; }
        public CashService CashService { get; }
        public int AdminId { get; }
        public int EmployeeId { get; }
        public int InactiveEmployeeId { get; }
        public int ProductId { get; }

        public static async Task<PermissionFixture> CreateAsync(
            bool seedEmployeePermissions = true,
            bool seedOpenCash = true)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var admin = CreateUser("Admin permisos", RolUsuario.ADMIN, active: true);
            var employee = CreateUser("Empleado permisos", RolUsuario.EMPLEADO, active: true);
            var inactive = CreateUser("Empleado inactivo", RolUsuario.EMPLEADO, active: false);
            var category = new Categoria
            {
                Nombre = $"Categoria-{Guid.NewGuid():N}",
                Activa = true,
                FechaCreacion = DateTime.UtcNow
            };

            context.Usuarios.AddRange(admin, employee, inactive);
            context.Categorias.Add(category);
            await context.SaveChangesAsync();

            var productCode = $"PERM-{Guid.NewGuid():N}";
            var product = new Producto
            {
                CodigoInterno = productCode,
                Nombre = "Producto permisos",
                CategoriaId = category.CategoriaId,
                PrecioCompraCentavos = 500,
                PrecioVentaCentavos = 1000,
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
            context.Productos.Add(product);
            if (seedOpenCash)
            {
                context.Cajas.Add(new Caja
                {
                    FechaOperacion = DateOnly.FromDateTime(DateTime.Now),
                    FechaApertura = DateTime.UtcNow,
                    MontoInicialCentavos = 0,
                    MontoCierreCentavos = 0,
                    Estado = EstadoCaja.ABIERTA,
                    UsuarioAperturaId = admin.UsuarioId
                });
            }
            await context.SaveChangesAsync();

            var session = new TestCurrentSession(new UsuarioSesion(
                admin.UsuarioId,
                admin.NombreCompleto,
                admin.NombreUsuario,
                admin.Rol));
            var audit = new AuditService(context);
            var permissionService = new EmployeePermissionService(context, session, audit);
            if (seedEmployeePermissions)
            {
                await permissionService.EnsureRecommendedPermissionsForEmployeeAsync(employee.UsuarioId);
                await permissionService.EnsureRecommendedPermissionsForEmployeeAsync(inactive.UsuarioId);
            }
            else
            {
                await permissionService.EnsureRecommendedPermissionsForEmployeeAsync(employee.UsuarioId);
                context.UsuarioPermisos.RemoveRange(context.UsuarioPermisos.Where(p => p.UsuarioId == employee.UsuarioId));
                await context.SaveChangesAsync();
                await permissionService.EnsureRecommendedPermissionsForEmployeeAsync(employee.UsuarioId);
            }

            var creditService = new CreditService(context, session, permissionService, audit);
            var saleService = new SaleService(context, session, audit, permissionService, creditService);
            var returnService = new ReturnService(context, session, audit, permissionService);
            var cashService = new CashService(context, session, audit, new NullNotificationService(), permissionService);

            return new PermissionFixture(
                connection,
                options,
                context,
                session,
                audit,
                permissionService,
                saleService,
                returnService,
                cashService,
                admin.UsuarioId,
                employee.UsuarioId,
                inactive.UsuarioId,
                product.ProductoId);
        }

        public void SignInAdmin()
        {
            SignIn(AdminId);
        }

        public void SignInEmployee()
        {
            SignIn(EmployeeId);
        }

        public void SignInInactiveEmployee()
        {
            SignIn(InactiveEmployeeId);
        }

        public async Task SetPermissionAsync(PermisoEmpleado permiso, bool enabled)
        {
            SignInAdmin();
            var states = await GetStoredPermissionStatesAsync(EmployeeId);
            if (enabled)
            {
                EnablePermissionWithDependencies(states, permiso);
            }
            else
            {
                DisablePermissionAndDependents(states, permiso);
            }

            await PermissionService.UpdatePermissionsAsync(new UpdateEmployeePermissionsRequest(
                EmployeeId,
                ToUpdates(states)));
        }

        public async Task SetExactPermissionsAsync(params PermisoEmpleado[] enabledPermissions)
        {
            SignInAdmin();
            var states = EmployeePermissionCatalog.AllPermissions.ToDictionary(permission => permission, _ => false);
            foreach (var permission in enabledPermissions)
            {
                EnablePermissionWithDependencies(states, permission);
            }

            await PermissionService.UpdatePermissionsAsync(new UpdateEmployeePermissionsRequest(
                EmployeeId,
                ToUpdates(states)));
        }

        public List<EmployeePermissionUpdate> ToUpdates(IReadOnlyDictionary<PermisoEmpleado, bool> states)
        {
            return EmployeePermissionCatalog.AllPermissions
                .OrderBy(permission => permission)
                .Select(permission => new EmployeePermissionUpdate(permission, states.GetValueOrDefault(permission)))
                .ToList();
        }

        public void EnablePermissionWithDependencies(
            IDictionary<PermisoEmpleado, bool> states,
            PermisoEmpleado permission)
        {
            if (EmployeePermissionCatalog.Dependencies.TryGetValue(permission, out var dependencies))
            {
                foreach (var dependency in dependencies)
                {
                    EnablePermissionWithDependencies(states, dependency);
                }
            }

            states[permission] = true;
        }

        public void DisablePermissionAndDependents(
            IDictionary<PermisoEmpleado, bool> states,
            PermisoEmpleado permission)
        {
            states[permission] = false;
            if (!EmployeePermissionCatalog.Dependents.TryGetValue(permission, out var dependents))
            {
                return;
            }

            foreach (var dependent in dependents)
            {
                DisablePermissionAndDependents(states, dependent);
            }
        }

        public async Task<bool> GetStoredPermissionAsync(int userId, PermisoEmpleado permiso)
        {
            return await Context.UsuarioPermisos
                .AsNoTracking()
                .Where(p => p.UsuarioId == userId && p.Permiso == permiso)
                .Select(p => p.Habilitado)
                .SingleAsync();
        }

        public async Task<Dictionary<PermisoEmpleado, bool>> GetStoredPermissionStatesAsync(int userId)
        {
            return await Context.UsuarioPermisos
                .AsNoTracking()
                .Where(p => p.UsuarioId == userId)
                .ToDictionaryAsync(p => p.Permiso, p => p.Habilitado);
        }

        public async Task<Dictionary<PermisoEmpleado, bool>> GetStoredPermissionStatesFromNewContextAsync(int userId)
        {
            await using var context = new AppDbContext(Options);
            return await context.UsuarioPermisos
                .AsNoTracking()
                .Where(p => p.UsuarioId == userId)
                .ToDictionaryAsync(p => p.Permiso, p => p.Habilitado);
        }

        public Task<SaleResult> CreateSaleAsync()
        {
            return SaleService.RegisterSaleAsync(new RegisterSaleRequest(
                [new SaleLineRequest(ProductId, 1)],
                [new SalePaymentRequest(MetodoPago.EFECTIVO, 1000)]));
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private void SignIn(int userId)
        {
            var user = Context.Usuarios.AsNoTracking().Single(u => u.UsuarioId == userId);
            CurrentSession.IniciarSesion(new UsuarioSesion(
                user.UsuarioId,
                user.NombreCompleto,
                user.NombreUsuario,
                user.Rol));
        }

        private static Usuario CreateUser(string name, RolUsuario role, bool active)
        {
            return new Usuario
            {
                NombreCompleto = name,
                NombreUsuario = $"{name.Replace(" ", "-").ToLowerInvariant()}-{Guid.NewGuid():N}",
                PasswordHash = "hash",
                Rol = role,
                Activo = active,
                FechaCreacion = DateTime.UtcNow
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
}
