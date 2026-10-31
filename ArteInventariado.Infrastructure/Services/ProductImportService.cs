using System.Globalization;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class ProductImportService : IProductImportService
{
    private static readonly string[] ExpectedHeaders =
    [
        "Codigo",
        "Nombre",
        "Categoria",
        "Descripcion",
        "PrecioCompra",
        "PrecioVenta",
        "StockActual",
        "StockMinimo"
    ];

    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _currentSession;
    private readonly IAuditService _auditService;
    private readonly IInstallationContext? _installationContext;
    private readonly IStationSettingsService? _stationSettingsService;

    [ActivatorUtilitiesConstructor]
    public ProductImportService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession currentSession,
        IAuditService auditService,
        IInstallationContext installationContext,
        IStationSettingsService stationSettingsService)
    {
        _contextFactory = contextFactory;
        _currentSession = currentSession;
        _auditService = auditService;
        _installationContext = installationContext;
        _stationSettingsService = stationSettingsService;
    }

    internal ProductImportService(AppDbContext context, ICurrentSession currentSession, IAuditService auditService)
    {
        _legacyContext = context;
        _currentSession = currentSession;
        _auditService = auditService;
    }

    internal ProductImportService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession currentSession,
        IAuditService auditService)
    {
        _contextFactory = contextFactory;
        _currentSession = currentSession;
        _auditService = auditService;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para importar productos.");

    public Task<string> CreateProductImportTemplateAsync(
        string? outputDirectory = null,
        CancellationToken cancellationToken = default)
    {
        EnsureAdminSession();

        var directory = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "ArteInventariado",
                "Plantillas")
            : outputDirectory.Trim();

        Directory.CreateDirectory(directory);
        var filePath = Path.Combine(directory, "Plantilla_Importacion_Productos.xlsx");

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Productos");

        for (var i = 0; i < ExpectedHeaders.Length; i++)
        {
            ws.Cell(1, i + 1).Value = ExpectedHeaders[i];
        }

        var headerRange = ws.Range(1, 1, 1, ExpectedHeaders.Length);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromArgb(217, 225, 242);
        headerRange.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        ws.SheetView.FreezeRows(1);

        ws.Columns(1, ExpectedHeaders.Length).AdjustToContents();
        ws.Columns(5, 6).Style.NumberFormat.Format = "$#,##0.00";
        ws.Columns(7, 8).Style.NumberFormat.Format = "0";

        workbook.SaveAs(filePath);
        return Task.FromResult(filePath);
    }

    public async Task<ProductImportPreviewResult> PreviewProductsAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        EnsureAdminSession();
        ValidateFilePath(filePath);

        var existingCodes = await GetExistingCodesAsync(cancellationToken);
        var categories = await GetActiveCategoryNamesAsync(cancellationToken);
        var parseResult = ParseWorkbook(filePath, existingCodes, categories);

        return new ProductImportPreviewResult(
            filePath,
            parseResult.Rows.Count,
            parseResult.Rows.Count(r => r.CanImport),
            parseResult.Rows.Count(r => !r.CanImport && !r.IsDuplicate),
            parseResult.Rows.Count(r => r.IsDuplicate),
            parseResult.GlobalErrors,
            parseResult.Rows);
    }

    public async Task<ProductImportResult> ImportProductsAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        _installationContext?.EnsureOperationalWriteAllowed("Importar productos");
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        EnsureAdminSession();

        var preview = await PreviewProductsAsync(filePath, cancellationToken);
        if (preview.GlobalErrors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, preview.GlobalErrors));
        }

        var backupPath = await CreateDatabaseBackupAsync(cancellationToken);
        var usuarioId = _currentSession.UsuarioActual!.UsuarioId;

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);

        var defaultCategory = await EnsureDefaultCategoryAsync(cancellationToken);
        var activeCategories = await Context.Categorias
            .Where(c => c.Activa)
            .ToDictionaryAsync(c => Normalize(c.Nombre), c => c.CategoriaId, cancellationToken);
        var existingCodes = await GetExistingCodesAsync(cancellationToken);
        var nextProductNumber = await GetNextProductNumberAsync(cancellationToken);
        var imported = 0;

        foreach (var row in preview.Rows.Where(r => r.CanImport))
        {
            var code = string.IsNullOrWhiteSpace(row.Codigo)
                ? GenerateCodigoInterno(existingCodes, ref nextProductNumber)
                : row.Codigo.Trim();

            if (existingCodes.Contains(code))
            {
                continue;
            }

            var categoryId = activeCategories.TryGetValue(Normalize(row.Categoria), out var matchedCategoryId)
                ? matchedCategoryId
                : defaultCategory.CategoriaId;

            var product = new Producto
            {
                CodigoInterno = code,
                Modo = ModoProducto.SIMPLE,
                Nombre = row.Nombre.Trim(),
                Descripcion = NormalizeOptional(row.Descripcion),
                CategoriaId = categoryId,
                PrecioCompraCentavos = DollarsToCentavos(row.PrecioCompra),
                PrecioVentaCentavos = DollarsToCentavos(row.PrecioVenta),
                StockMinimo = row.StockMinimo,
                Estado = EstadoProducto.ACTIVO,
                FechaCreacion = DateTime.UtcNow,
                Skus =
                {
                    new ProductoSku
                    {
                        CodigoSku = code,
                        Activo = true,
                        EsPrincipal = true
                    }
                }
            };

            var primarySku = product.Skus.Single();
            foreach (var balance in InventoryStockEngine.CreateInitialBalances(primarySku, row.StockActual))
            {
                primarySku.SaldosInventario.Add(balance);
            }

            Context.Productos.Add(product);
            existingCodes.Add(code);
            imported++;
        }

        var importacion = new ImportacionExcel
        {
            NombreArchivo = Path.GetFileName(filePath),
            RutaArchivo = Path.GetFullPath(filePath),
            UsuarioId = usuarioId,
            FechaImportacion = DateTime.UtcNow,
            FilasLeidas = preview.FilasLeidas,
            FilasImportadas = imported,
            FilasConError = preview.FilasConError,
            Estado = imported > 0 ? EstadoImportacionExcel.COMPLETADA : EstadoImportacionExcel.FALLIDA,
            ResumenErrores = BuildErrorSummary(preview)
        };

        Context.ImportacionesExcel.Add(importacion);
        await Context.SaveChangesAsync(cancellationToken);

        await _auditService.RegistrarAsync(
            new AuditEntryRequest(
                usuarioId,
                TipoBitacora.IMPORTACION_EXCEL,
                "importaciones_excel",
                importacion.ImportacionExcelId,
                $"Importacion de productos desde Excel. Archivo: {Path.GetFileName(filePath)}. Importados: {imported}. Errores: {preview.FilasConError}. Duplicados ignorados: {preview.Duplicados}. Respaldo: {backupPath}"),
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new ProductImportResult(
            importacion.ImportacionExcelId,
            Path.GetFullPath(filePath),
            backupPath,
            preview.FilasLeidas,
            imported,
            preview.FilasConError,
            preview.Duplicados);
    }

    private static WorkbookParseResult ParseWorkbook(
        string filePath,
        HashSet<string> existingCodes,
        HashSet<string> activeCategories)
    {
        using var workbook = new XLWorkbook(filePath);
        var ws = workbook.Worksheets.FirstOrDefault();
        if (ws is null)
        {
            return new WorkbookParseResult([], ["El archivo no contiene hojas."]);
        }

        var headerErrors = ValidateHeaders(ws);
        if (headerErrors.Count > 0)
        {
            return new WorkbookParseResult([], headerErrors);
        }

        var usedRange = ws.RangeUsed();
        if (usedRange is null || usedRange.LastRow().RowNumber() < 2)
        {
            return new WorkbookParseResult([], ["La plantilla no contiene filas de productos."]);
        }

        var rows = new List<ProductImportPreviewRow>();
        var codesInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var rowNumber = 2; rowNumber <= usedRange.LastRow().RowNumber(); rowNumber++)
        {
            if (IsEmptyRow(ws, rowNumber))
            {
                continue;
            }

            var codigo = ReadText(ws.Cell(rowNumber, 1));
            var nombre = ReadText(ws.Cell(rowNumber, 2)) ?? string.Empty;
            var categoriaOriginal = ReadText(ws.Cell(rowNumber, 3));
            var descripcion = ReadText(ws.Cell(rowNumber, 4));
            var categoria = string.IsNullOrWhiteSpace(categoriaOriginal)
                ? CategoryService.DefaultCategoryName
                : categoriaOriginal.Trim();

            var errors = new List<string>();
            var notes = new List<string>();

            if (string.IsNullOrWhiteSpace(nombre))
            {
                errors.Add("Nombre obligatorio.");
            }

            var precioCompra = ReadDecimal(ws.Cell(rowNumber, 5), "PrecioCompra", errors);
            var precioVenta = ReadDecimal(ws.Cell(rowNumber, 6), "PrecioVenta", errors);
            var stockActual = ReadInt(ws.Cell(rowNumber, 7), "StockActual", errors);
            var stockMinimo = ReadInt(ws.Cell(rowNumber, 8), "StockMinimo", errors);

            if (precioCompra < 0)
            {
                errors.Add("PrecioCompra no puede ser negativo.");
            }

            if (precioVenta < 0)
            {
                errors.Add("PrecioVenta no puede ser negativo.");
            }

            if (stockActual < 0)
            {
                errors.Add("StockActual no puede ser negativo.");
            }

            if (stockMinimo < 0)
            {
                errors.Add("StockMinimo no puede ser negativo.");
            }

            var duplicate = false;
            if (!string.IsNullOrWhiteSpace(codigo))
            {
                var normalizedCode = codigo.Trim();
                if (existingCodes.Contains(normalizedCode))
                {
                    duplicate = true;
                    notes.Add("Codigo ya existe en el sistema.");
                }
                else if (!codesInFile.Add(normalizedCode))
                {
                    duplicate = true;
                    notes.Add("Codigo duplicado dentro del archivo.");
                }
            }
            else
            {
                notes.Add("Codigo automatico al importar.");
            }

            if (string.IsNullOrWhiteSpace(categoriaOriginal))
            {
                notes.Add("Categoria vacia: se usara Sin categoria.");
            }
            else if (!activeCategories.Contains(categoria))
            {
                notes.Add("Categoria no existe: se usara Sin categoria.");
                categoria = CategoryService.DefaultCategoryName;
            }

            var canImport = errors.Count == 0 && !duplicate;
            var estado = duplicate
                ? "Duplicado"
                : canImport
                    ? "Valido"
                    : "Error";

            rows.Add(new ProductImportPreviewRow(
                rowNumber,
                string.IsNullOrWhiteSpace(codigo) ? null : codigo.Trim(),
                nombre.Trim(),
                categoria,
                descripcion,
                precioCompra,
                precioVenta,
                stockActual,
                stockMinimo,
                estado,
                notes.Count == 0 ? null : string.Join(" ", notes),
                canImport,
                duplicate,
                errors.Count == 0 ? null : string.Join(" ", errors)));
        }

        return new WorkbookParseResult(rows, []);
    }

    private static List<string> ValidateHeaders(IXLWorksheet ws)
    {
        var errors = new List<string>();

        for (var i = 0; i < ExpectedHeaders.Length; i++)
        {
            var actual = ReadText(ws.Cell(1, i + 1));
            if (!string.Equals(actual, ExpectedHeaders[i], StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Encabezado invalido en columna {i + 1}. Esperado: {ExpectedHeaders[i]}.");
            }
        }

        return errors;
    }

    private async Task<string> CreateDatabaseBackupAsync(CancellationToken cancellationToken)
    {
        var backupDirectory = _stationSettingsService is null
            ? BackupPathProvider.GetBackupDirectory()
            : await _stationSettingsService.GetBackupDirectoryAsync(cancellationToken);

        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(
            backupDirectory,
            $"arteinventariado_v1_pre_import_{DateTime.Now:yyyyMMdd_HHmmss_fff}.db");

        await Context.Database.ExecuteSqlInterpolatedAsync($"VACUUM INTO {backupPath}", cancellationToken);
        return backupPath;
    }

    private async Task<Categoria> EnsureDefaultCategoryAsync(CancellationToken cancellationToken)
    {
        var categoria = await Context.Categorias
            .FirstOrDefaultAsync(c => c.Nombre == CategoryService.DefaultCategoryName, cancellationToken);

        if (categoria is not null)
        {
            if (!categoria.Activa)
            {
                categoria.Activa = true;
                categoria.FechaModificacion = DateTime.UtcNow;
                await Context.SaveChangesAsync(cancellationToken);
            }

            return categoria;
        }

        categoria = new Categoria
        {
            Nombre = CategoryService.DefaultCategoryName,
            Descripcion = "Categoria predeterminada para productos sin categoria.",
            Activa = true,
            FechaCreacion = DateTime.UtcNow
        };

        Context.Categorias.Add(categoria);
        await Context.SaveChangesAsync(cancellationToken);
        return categoria;
    }

    private async Task<HashSet<string>> GetExistingCodesAsync(CancellationToken cancellationToken)
    {
        var codes = await Context.Productos
            .AsNoTracking()
            .Select(p => p.CodigoInterno)
            .ToListAsync(cancellationToken);

        codes.AddRange(await Context.ProductoSkus
            .AsNoTracking()
            .Select(sku => sku.CodigoSku)
            .ToListAsync(cancellationToken));

        return new HashSet<string>(codes, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<HashSet<string>> GetActiveCategoryNamesAsync(CancellationToken cancellationToken)
    {
        var categories = await Context.Categorias
            .AsNoTracking()
            .Where(c => c.Activa)
            .Select(c => c.Nombre)
            .ToListAsync(cancellationToken);

        categories.Add(CategoryService.DefaultCategoryName);
        return new HashSet<string>(categories, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<int> GetNextProductNumberAsync(CancellationToken cancellationToken)
    {
        var maxId = await Context.Productos
            .AsNoTracking()
            .MaxAsync(p => (int?)p.ProductoId, cancellationToken);

        return (maxId ?? 0) + 1;
    }

    private static string GenerateCodigoInterno(HashSet<string> existingCodes, ref int nextProductNumber)
    {
        while (true)
        {
            var code = $"PRD-{nextProductNumber:000000}";
            nextProductNumber++;

            if (existingCodes.Add(code))
            {
                return code;
            }
        }
    }

    private static bool IsEmptyRow(IXLWorksheet ws, int rowNumber)
    {
        for (var column = 1; column <= ExpectedHeaders.Length; column++)
        {
            if (!string.IsNullOrWhiteSpace(ReadText(ws.Cell(rowNumber, column))))
            {
                return false;
            }
        }

        return true;
    }

    private static string? ReadText(IXLCell cell)
    {
        var value = cell.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static decimal ReadDecimal(IXLCell cell, string columnName, List<string> errors)
    {
        if (cell.IsEmpty())
        {
            return 0;
        }

        try
        {
            return cell.GetValue<decimal>();
        }
        catch
        {
            var text = ReadText(cell);
            if (TryParseDecimal(text, out var value))
            {
                return value;
            }
        }

        errors.Add($"{columnName} debe ser un numero valido.");
        return 0;
    }

    private static int ReadInt(IXLCell cell, string columnName, List<string> errors)
    {
        if (cell.IsEmpty())
        {
            return 0;
        }

        try
        {
            return cell.GetValue<int>();
        }
        catch
        {
            var text = ReadText(cell);
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
            {
                return intValue;
            }
        }

        errors.Add($"{columnName} debe ser un numero entero valido.");
        return 0;
    }

    private static bool TryParseDecimal(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var styles = NumberStyles.Number | NumberStyles.AllowCurrencySymbol;
        return decimal.TryParse(text, styles, CultureInfo.CurrentCulture, out value)
            || decimal.TryParse(text, styles, CultureInfo.GetCultureInfo("es-SV"), out value)
            || decimal.TryParse(text, styles, CultureInfo.InvariantCulture, out value);
    }

    private static long DollarsToCentavos(decimal dollars)
    {
        return decimal.ToInt64(decimal.Round(dollars * 100m, 0, MidpointRounding.AwayFromZero));
    }

    private static string? BuildErrorSummary(ProductImportPreviewResult preview)
    {
        var issues = preview.Rows
            .Where(r => !r.CanImport)
            .Take(30)
            .Select(r => $"Fila {r.RowNumber}: {r.Error ?? r.Observaciones ?? r.Estado}")
            .ToList();

        return issues.Count == 0
            ? null
            : string.Join(Environment.NewLine, issues);
    }

    private static void ValidateFilePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Debe seleccionar un archivo .xlsx.", nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("El archivo seleccionado no existe.", filePath);
        }

        if (!string.Equals(Path.GetExtension(filePath), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("La importacion V1 solo acepta archivos .xlsx normalizados.");
        }
    }

    private void EnsureAdminSession()
    {
        if (!_currentSession.EsAdmin || _currentSession.UsuarioActual is null)
        {
            throw new InvalidOperationException("Solo un usuario ADMIN puede importar productos desde Excel.");
        }
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToUpperInvariant();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record WorkbookParseResult(
        IReadOnlyList<ProductImportPreviewRow> Rows,
        IReadOnlyList<string> GlobalErrors);
}
