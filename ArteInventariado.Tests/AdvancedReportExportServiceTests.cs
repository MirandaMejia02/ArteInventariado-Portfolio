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

public class AdvancedReportExportServiceTests
{
    [Fact]
    public async Task Excel_IncludesMetadataHeadersFormatsTotalsAndNoSensitiveColumns()
    {
        await using var fixture = await ExportFixture.CreateAsync(withRows: true);
        var request = Request() with { Search = "../cliente:*?" };

        var result = await fixture.Service.ExportExcelAsync(request);

        Assert.StartsWith(Path.GetFullPath(fixture.OutputDirectory), Path.GetFullPath(result.FilePath));
        Assert.Equal(2, result.ExportedRows);
        Assert.DoesNotContain("..", Path.GetFileName(result.FilePath));
        using var workbook = new XLWorkbook(result.FilePath);
        var sheet = workbook.Worksheet(1);
        Assert.Equal("Negocio de pruebas", sheet.Cell(1, 1).GetString());
        Assert.Contains("Ventas", sheet.CellsUsed().Select(cell => cell.GetString()));
        Assert.Contains(sheet.CellsUsed(), cell => cell.GetString().Contains("../cliente:*?", StringComparison.Ordinal));

        var header = sheet.CellsUsed().Single(cell => cell.GetString() == "Monto");
        var dataCell = sheet.Cell(header.Address.RowNumber + 1, header.Address.ColumnNumber);
        Assert.Equal(AdvancedReportExportService.CurrencyFormat, dataCell.Style.NumberFormat.Format);
        var percentageHeader = sheet.CellsUsed().Single(cell => cell.GetString() == "Margen");
        Assert.Equal(
            AdvancedReportExportService.PercentageFormat,
            sheet.Cell(percentageHeader.Address.RowNumber + 1, percentageHeader.Address.ColumnNumber).Style.NumberFormat.Format);

        var totalsLabel = sheet.CellsUsed().Single(cell => cell.GetString() == "Totales");
        Assert.Equal(35m, sheet.Cell(totalsLabel.Address.RowNumber, header.Address.ColumnNumber).GetValue<decimal>());
        var allText = string.Join("|", sheet.CellsUsed().Select(cell => cell.GetString()));
        Assert.DoesNotContain("PasswordHash", allText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DatosAntes", allText, StringComparison.OrdinalIgnoreCase);
        Assert.Single(fixture.Audit.Entries);
    }

    [Fact]
    public async Task Excel_EmptyReportWritesExplicitEmptyState()
    {
        await using var fixture = await ExportFixture.CreateAsync(withRows: false);

        var result = await fixture.Service.ExportExcelAsync(Request());

        using var workbook = new XLWorkbook(result.FilePath);
        Assert.Contains(
            "Sin datos para los filtros seleccionados.",
            workbook.Worksheet(1).CellsUsed().Select(cell => cell.GetString()));
        Assert.Equal(0, result.ExportedRows);
    }

    [Fact]
    public void Excel_CategoryReportContainsCategorySubcategoryAndFilterMetadata()
    {
        var period = new AnalyticsPeriodDefinition(
            AnalyticsPeriodKind.CustomRange,
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(-6)),
            new DateTimeOffset(2026, 8, 13, 0, 0, 0, TimeSpan.FromHours(-6)),
            new DateTime(2026, 8, 1, 6, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 13, 6, 0, 0, DateTimeKind.Utc),
            AnalyticsGrouping.Day,
            "01/08/2026 al 12/08/2026",
            false);
        var report = new AdvancedReportResult(
            AdvancedReportCatalog.Get(AdvancedReportKind.Categories),
            period,
            ["Categoría: Madera", "Subcategoría: Madera fina"],
            [
                new("category", "Categoría"),
                new("subcategory", "Subcategoría"),
                new("net", "Ventas netas", AdvancedReportValueKind.Money, true)
            ],
            [new(new Dictionary<string, object?>
            {
                ["category"] = "Madera",
                ["subcategory"] = "Madera fina",
                ["net"] = 9.50m
            })],
            1,
            [],
            null,
            "Sin datos");

        using var workbook = AdvancedReportExportService.BuildWorkbook(
            report,
            report.Rows,
            "Negocio",
            "admin",
            new DateTime(2026, 8, 12, 18, 30, 0));
        var cells = workbook.Worksheet(1).CellsUsed().Select(cell => cell.GetString()).ToList();
        Assert.Contains("Categoría", cells);
        Assert.Contains("Subcategoría", cells);
        Assert.Contains("Madera fina", cells);
        Assert.Contains(cells, value => value.Contains("Categoría: Madera", StringComparison.Ordinal));
        Assert.Contains(cells, value => value.Contains("Subcategoría: Madera fina", StringComparison.Ordinal));
    }

    [Fact]
    public void Excel_CurrentInventoryValueContainsGeneralCatalogAndHistoricalSheets()
    {
        var period = new AnalyticsPeriodDefinition(
            AnalyticsPeriodKind.AllHistory,
            new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 13, 0, 0, 0, TimeSpan.Zero),
            new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 13, 0, 0, 0, DateTimeKind.Utc),
            AnalyticsGrouping.Year,
            "Todo el historial",
            true);
        var definition = AdvancedReportCatalog.Get(AdvancedReportKind.CurrentInventoryValue);
        var columns = new[]
        {
            new AdvancedReportColumn("productId", "Producto ID"),
            new AdvancedReportColumn("code", "Código"),
            new AdvancedReportColumn("sku", "SKU"),
            new AdvancedReportColumn("product", "Producto"),
            new AdvancedReportColumn("variant", "Variante"),
            new AdvancedReportColumn("category", "Categoría"),
            new AdvancedReportColumn("subcategory", "Subcategoría"),
            new AdvancedReportColumn("state", "Estado"),
            new AdvancedReportColumn("stock", "Stock físico", AdvancedReportValueKind.Integer, true),
            new AdvancedReportColumn("reserved", "Reservado", AdvancedReportValueKind.Integer, true),
            new AdvancedReportColumn("available", "Disponible", AdvancedReportValueKind.Integer, true),
            new AdvancedReportColumn("exhibition", "En exhibición", AdvancedReportValueKind.Integer, true),
            new AdvancedReportColumn("warehouse", "En bodega", AdvancedReportValueKind.Integer, true),
            new AdvancedReportColumn("unassigned", "Sin asignar", AdvancedReportValueKind.Integer, true),
            new AdvancedReportColumn("cost", "Costo", AdvancedReportValueKind.Money),
            new AdvancedReportColumn("price", "Precio", AdvancedReportValueKind.Money),
            new AdvancedReportColumn("value", "Valor actual", AdvancedReportValueKind.Money, true),
            new AdvancedReportColumn("investment", "Inversión actual", AdvancedReportValueKind.Money, true),
            new AdvancedReportColumn("potential", "Venta potencial", AdvancedReportValueKind.Money, true),
            new AdvancedReportColumn("availableValue", "Venta disponible", AdvancedReportValueKind.Money, true),
            new AdvancedReportColumn("gain", "Ganancia", AdvancedReportValueKind.Money, true),
            new AdvancedReportColumn("margin", "Margen", AdvancedReportValueKind.Percentage)
        };
        var row = new AdvancedReportRow(new Dictionary<string, object?>
        {
            ["productId"] = 1, ["code"] = "P-1", ["sku"] = "SKU-1", ["product"] = "Producto 1",
            ["variant"] = "", ["category"] = "Madera", ["subcategory"] = "Sin subcategoría",
            ["state"] = "ACTIVO / SKU activo", ["stock"] = 10, ["reserved"] = 2, ["available"] = 8,
            ["exhibition"] = 6, ["warehouse"] = 3, ["unassigned"] = 1, ["cost"] = 3m,
            ["price"] = 5m, ["value"] = 30m, ["investment"] = 30m, ["potential"] = 50m,
            ["availableValue"] = 40m, ["gain"] = 20m, ["margin"] = 40m,
            ["locationQuality"] = "Pendiente de ubicación", ["quality"] = "Pendiente de ubicación"
        });
        var report = new AdvancedReportResult(
            definition,
            period,
            ["Sin filtros específicos"],
            columns,
            [row],
            1,
            [
                new AdvancedReportSummaryItem("Inversión actual", 30m, AdvancedReportValueKind.Money),
                new AdvancedReportSummaryItem("Venta potencial física", 50m, AdvancedReportValueKind.Money),
                new AdvancedReportSummaryItem("Venta disponible", 40m, AdvancedReportValueKind.Money),
                new AdvancedReportSummaryItem("Ganancia bruta potencial", 20m, AdvancedReportValueKind.Money),
                new AdvancedReportSummaryItem("Margen bruto potencial", 40m, AdvancedReportValueKind.Percentage)
            ],
            "Fuente: inventario_saldos.",
            "Sin datos");
        var history = new ProductProfitabilityReport(
            period,
            [new ProductProfitabilityRow(
                1, "P-1", "Producto 1", "Madera", 2, 0, 500m, 300m, 1000,
                600, 600, 200m, 400, 40m, 100m,
                ProductProfitabilityCoverageState.Complete, 1, ["SKU-1"], null,
                "Sin subcategoría", new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc))],
            1, 1, 1000, 600, 400, 40m, 100m, 1, 0, 0, 0, "Cobertura completa");

        using var workbook = AdvancedReportExportService.BuildInventoryValuationWorkbook(
            report, [row], "Negocio", "admin", new DateTime(2026, 8, 12, 18, 30, 0), history, null);

        var names = workbook.Worksheets.Select(sheet => sheet.Name).ToList();
        Assert.Equal(7, names.Count);
        Assert.Contains("Resumen global", names);
        Assert.Contains("Todos los productos", names);
        Assert.Contains("Detalle SKU", names);
        Assert.Contains("Por categoría", names);
        Assert.Contains("Por subcategoría", names);
        Assert.Contains("Rentabilidad histórica", names);
        Assert.Contains("Definiciones y cobertura", names);
        var allProducts = workbook.Worksheet("Todos los productos");
        Assert.Contains(allProducts.CellsUsed(), cell => cell.GetString() == "Ganancia bruta potencial");
        Assert.Contains(allProducts.CellsUsed(), cell => cell.GetString() == "Sin subcategoría");
        Assert.Equal("TodosProductos", Assert.Single(allProducts.Tables).Name);
        Assert.Equal("DetalleReporte", Assert.Single(workbook.Worksheet("Detalle SKU").Tables).Name);
        Assert.Equal("ResumenGlobal", Assert.Single(workbook.Worksheet("Resumen global").Tables).Name);
        var summary = workbook.Worksheet("Resumen global");
        Assert.Contains(summary.CellsUsed(), cell => cell.GetString() == "Inventario valorado al");
        Assert.Contains(summary.CellsUsed(), cell => cell.GetString() == "Margen bruto potencial");
        Assert.Contains(
            summary.CellsUsed(),
            cell => cell.GetString().Contains("01/01/2026", StringComparison.Ordinal));
        var historicalSalesLabel = summary.CellsUsed().Single(cell => cell.GetString() == "Ventas netas históricas");
        Assert.Equal(10m, summary.Cell(historicalSalesLabel.Address.RowNumber, 2).GetValue<decimal>());
        var historicalCostLabel = summary.CellsUsed().Single(cell => cell.GetString() == "Costo histórico vendido cubierto");
        Assert.Equal(6m, summary.Cell(historicalCostLabel.Address.RowNumber, 2).GetValue<decimal>());
        var historicalGainLabel = summary.CellsUsed().Single(cell => cell.GetString() == "Ganancia bruta histórica realizada");
        Assert.Equal(4m, summary.Cell(historicalGainLabel.Address.RowNumber, 2).GetValue<decimal>());
    }

    [Fact]
    public void Excel_AllHistoryWithoutSaleSnapshotDoesNotExposeEpochSentinel()
    {
        var period = new AnalyticsPeriodDefinition(
            AnalyticsPeriodKind.AllHistory,
            new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 13, 0, 0, 0, TimeSpan.Zero),
            new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 13, 0, 0, 0, DateTimeKind.Utc),
            AnalyticsGrouping.Year,
            "Todo el historial",
            true);
        var definition = AdvancedReportCatalog.Get(AdvancedReportKind.CurrentInventoryValue);
        var columns = new[]
        {
            new AdvancedReportColumn("productId", "Producto ID"),
            new AdvancedReportColumn("code", "Código"),
            new AdvancedReportColumn("sku", "SKU"),
            new AdvancedReportColumn("product", "Producto"),
            new AdvancedReportColumn("category", "Categoría"),
            new AdvancedReportColumn("subcategory", "Subcategoría"),
            new AdvancedReportColumn("stock", "Stock físico", AdvancedReportValueKind.Integer, true),
            new AdvancedReportColumn("investment", "Inversión actual", AdvancedReportValueKind.Money, true),
            new AdvancedReportColumn("potential", "Venta potencial", AdvancedReportValueKind.Money, true)
        };
        var row = new AdvancedReportRow(new Dictionary<string, object?>
        {
            ["productId"] = 1, ["code"] = "P-1", ["sku"] = "SKU-1", ["product"] = "Producto 1",
            ["category"] = "Madera", ["subcategory"] = "Sin subcategoría", ["stock"] = 0,
            ["investment"] = 0m, ["potential"] = 0m, ["quality"] = "Exacto"
        });
        var report = new AdvancedReportResult(
            definition,
            period,
            ["Sin filtros específicos"],
            columns,
            [row],
            1,
            [new AdvancedReportSummaryItem("Inversión actual", 0m, AdvancedReportValueKind.Money)],
            "Sin movimientos históricos.",
            "Sin datos");
        var history = new ProductProfitabilityReport(
            period,
            [new ProductProfitabilityRow(
                1, "P-1", "Producto 1", "Madera", 0, 0, null, null, 0,
                null, null, null, null, null, null,
                ProductProfitabilityCoverageState.NoSales, 1, ["SKU-1"], null,
                "Sin subcategoría")],
            1, 0, 0, null, null, null, null, 0, 0, 0, 1, "Sin ventas");

        using var workbook = AdvancedReportExportService.BuildInventoryValuationWorkbook(
            report, [row], "Negocio", "admin", new DateTime(2026, 8, 12, 18, 30, 0), history, null);
        var summaryText = workbook.Worksheet("Resumen global").CellsUsed()
            .Select(cell => cell.GetString())
            .Single(value => value.StartsWith("Todo el historial:", StringComparison.Ordinal));

        Assert.Contains("desde el primer registro válido", summaryText, StringComparison.Ordinal);
        Assert.DoesNotContain("1970", summaryText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Excel_HonorsCancellationBeforeCreatingFile()
    {
        await using var fixture = await ExportFixture.CreateAsync(withRows: true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Service.ExportExcelAsync(Request(), cancellation.Token));

        Assert.Empty(Directory.GetFiles(fixture.OutputDirectory, "*.xlsx", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Excel_RequiresActiveAdminAndSanitizesFileNames()
    {
        await using var fixture = await ExportFixture.CreateAsync(withRows: true);
        fixture.Session.SetRole(RolUsuario.EMPLEADO);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => fixture.Service.ExportExcelAsync(Request()));

        var safe = AdvancedReportExportService.SanitizeFileName("../../Reporte:cliente\\2026");
        Assert.DoesNotContain("..", safe);
        Assert.DoesNotContain('/', safe);
        Assert.DoesNotContain('\\', safe);
    }

    private static AdvancedReportRequest Request() => new(
        AdvancedReportKind.Sales,
        new AnalyticsQueryRequest(new AnalyticsPeriodRequest(
            AnalyticsPeriodKind.CustomRange,
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 12))));

    private sealed class ExportFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private ExportFixture(
            SqliteConnection connection,
            AppDbContext context,
            AdvancedReportExportService service,
            MutableSession session,
            FakeAuditService audit,
            string outputDirectory)
        {
            _connection = connection;
            Context = context;
            Service = service;
            Session = session;
            Audit = audit;
            OutputDirectory = outputDirectory;
        }

        public AppDbContext Context { get; }
        public AdvancedReportExportService Service { get; }
        public MutableSession Session { get; }
        public FakeAuditService Audit { get; }
        public string OutputDirectory { get; }

        public static async Task<ExportFixture> CreateAsync(bool withRows)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();
            var admin = new Usuario
            {
                NombreCompleto = "Admin exportación",
                NombreUsuario = "admin-export",
                PasswordHash = "hash-secreto",
                Rol = RolUsuario.ADMIN,
                Activo = true,
                FechaCreacion = DateTime.UtcNow
            };
            context.Usuarios.Add(admin);
            context.ConfiguracionSistema.Add(new ConfiguracionSistema
            {
                Clave = SystemConfigService.BusinessNameKey,
                Valor = "Negocio de pruebas",
                FechaModificacion = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var output = Path.Combine(Path.GetTempPath(), "ArteInventariado-advanced-export-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(output);
            var session = new MutableSession(new UsuarioSesion(
                admin.UsuarioId, admin.NombreCompleto, admin.NombreUsuario, RolUsuario.ADMIN));
            var audit = new FakeAuditService();
            var service = new AdvancedReportExportService(
                context,
                new FakeQueryService(withRows),
                session,
                audit,
                new FakePathProvider(output),
                new FixedTimeProvider(new DateTimeOffset(2026, 8, 12, 18, 30, 0, TimeSpan.Zero)));
            return new ExportFixture(connection, context, service, session, audit, output);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
            if (Directory.Exists(OutputDirectory)) Directory.Delete(OutputDirectory, recursive: true);
        }
    }

    private sealed class FakeQueryService(bool withRows) : IAdvancedReportQueryService
    {
        public Task<AdvancedReportResult> QueryAsync(
            AdvancedReportRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var period = new AnalyticsPeriodDefinition(
                AnalyticsPeriodKind.CustomRange,
                new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(-6)),
                new DateTimeOffset(2026, 8, 13, 0, 0, 0, TimeSpan.FromHours(-6)),
                new DateTime(2026, 8, 1, 6, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 8, 13, 6, 0, 0, DateTimeKind.Utc),
                AnalyticsGrouping.Day,
                "01/08/2026 al 12/08/2026",
                false);
            IReadOnlyList<AdvancedReportRow> rows = withRows
                ?
                [
                    new(new Dictionary<string, object?> { ["name"] = "Cliente A", ["amount"] = 10m, ["margin"] = 40m }),
                    new(new Dictionary<string, object?> { ["name"] = "Cliente B", ["amount"] = 25m, ["margin"] = 50m })
                ]
                : [];
            return Task.FromResult(new AdvancedReportResult(
                AdvancedReportCatalog.Get(AdvancedReportKind.Sales),
                period,
                string.IsNullOrWhiteSpace(request.Search) ? ["Sin filtros específicos"] : [$"Búsqueda: {request.Search}"],
                [
                    new("name", "Cliente"),
                    new("amount", "Monto", AdvancedReportValueKind.Money, true),
                    new("margin", "Margen", AdvancedReportValueKind.Percentage)
                ],
                rows,
                rows.Count,
                [new("Ventas netas", 35m, AdvancedReportValueKind.Money)],
                "Cobertura histórica: 82%.",
                "No existen ventas en este período."));
        }
    }

    private sealed class FakePathProvider(string root) : IReportPathProvider
    {
        public Task<string> GetRootDirectoryAsync(CancellationToken cancellationToken = default) => Task.FromResult(root);
        public Task<string> GetDirectoryAsync(string category, CancellationToken cancellationToken = default)
        {
            var directory = Path.Combine(root, category);
            Directory.CreateDirectory(directory);
            return Task.FromResult(directory);
        }
    }

    private sealed class FakeAuditService : IAuditService
    {
        public List<AuditEntryRequest> Entries { get; } = [];
        public Task RegistrarAsync(AuditEntryRequest request, CancellationToken cancellationToken = default)
        {
            Entries.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MutableSession(UsuarioSesion user) : ICurrentSession
    {
        public UsuarioSesion? UsuarioActual { get; private set; } = user;
        public bool EstaAutenticado => UsuarioActual is not null;
        public bool EsAdmin => UsuarioActual?.Rol == RolUsuario.ADMIN;
        public bool TieneRol(RolUsuario rol) => UsuarioActual?.Rol == rol;
        public void IniciarSesion(UsuarioSesion usuario) => UsuarioActual = usuario;
        public void CerrarSesion() => UsuarioActual = null;
        public void SetRole(RolUsuario role) => UsuarioActual = UsuarioActual! with { Rol = role };
    }
}
