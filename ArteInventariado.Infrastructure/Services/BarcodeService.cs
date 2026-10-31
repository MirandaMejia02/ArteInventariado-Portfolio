using System.Security.Cryptography;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public sealed class BarcodeService : IBarcodeService
{
    private const int MaxCodeLength = 128;
    private const int GenerationAttempts = 10;
    private const int BulkOperationAttempts = 3;

    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly IEmployeePermissionService _permissions;
    private readonly IInstallationContext _installation;
    private readonly ICurrentSession _session;
    private readonly IAuditService _audit;
    private readonly Func<string> _codeGenerator;

    [ActivatorUtilitiesConstructor]
    public BarcodeService(
        IDbContextFactory<AppDbContext> contextFactory,
        IEmployeePermissionService permissions,
        IInstallationContext installation,
        ICurrentSession session,
        IAuditService audit)
    {
        _contextFactory = contextFactory;
        _permissions = permissions;
        _installation = installation;
        _session = session;
        _audit = audit;
        _codeGenerator = GenerateCode;
    }

    internal BarcodeService(
        AppDbContext context,
        IEmployeePermissionService permissions,
        IInstallationContext installation,
        ICurrentSession session,
        IAuditService audit,
        Func<string>? codeGenerator = null)
    {
        _legacyContext = context;
        _permissions = permissions;
        _installation = installation;
        _session = session;
        _audit = audit;
        _codeGenerator = codeGenerator ?? GenerateCode;
    }

    public async Task<IReadOnlyList<ProductSkuBarcodeSummary>> GetBySkuAsync(
        int productoSkuId,
        CancellationToken cancellationToken = default)
    {
        if (productoSkuId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(productoSkuId), "El SKU no es valido.");
        }

        await using var lease = await CreateContextAsync(cancellationToken);
        if (!await lease.Context.ProductoSkus.AsNoTracking()
                .AnyAsync(sku => sku.ProductoSkuId == productoSkuId, cancellationToken))
        {
            throw new InvalidOperationException("El SKU no existe.");
        }

        return await lease.Context.ProductoSkuBarcodes
            .AsNoTracking()
            .Where(barcode => barcode.ProductoSkuId == productoSkuId)
            .OrderByDescending(barcode => barcode.EsPrincipal)
            .ThenByDescending(barcode => barcode.Activo)
            .ThenBy(barcode => barcode.Codigo)
            .Select(barcode => new ProductSkuBarcodeSummary(
                barcode.ProductoSkuBarcodeId,
                barcode.ProductoSkuId,
                barcode.Codigo,
                barcode.Activo,
                barcode.EsPrincipal,
                barcode.Symbology))
            .ToListAsync(cancellationToken);
    }

    public async Task<BarcodeLookupResult?> LookupExactAsync(
        string codigo,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeLookupCode(codigo);
        if (normalized is null)
        {
            return null;
        }

        await using var lease = await CreateContextAsync(cancellationToken);
        return await lease.Context.ProductoSkuBarcodes
            .AsNoTracking()
            .Where(barcode =>
                barcode.Codigo == normalized &&
                barcode.Activo &&
                barcode.ProductoSku.Activo &&
                barcode.ProductoSku.Producto.Estado == EstadoProducto.ACTIVO)
            .Select(barcode => new BarcodeLookupResult(
                barcode.ProductoSkuBarcodeId,
                barcode.Codigo,
                barcode.ProductoSku.ProductoId,
                barcode.ProductoSku.Producto.CodigoInterno,
                barcode.ProductoSku.Producto.Nombre,
                barcode.ProductoSku.Producto.Modo,
                barcode.ProductoSkuId,
                barcode.ProductoSku.CodigoSku,
                barcode.ProductoSku.NombreVariante,
                barcode.ProductoSku.PrecioVentaOverrideCentavos ??
                    barcode.ProductoSku.Producto.PrecioVentaCentavos,
                barcode.ProductoSku.CostoOverrideCentavos ??
                    barcode.ProductoSku.Producto.PrecioCompraCentavos,
                barcode.ProductoSku.SaldosInventario
                    .Where(balance => balance.Ubicacion == UbicacionInventario.EXHIBICION)
                    .Sum(balance => balance.ExistenciaFisica),
                barcode.ProductoSku.SaldosInventario
                    .Where(balance => balance.Ubicacion == UbicacionInventario.EXHIBICION)
                    .Sum(balance => balance.Reservado),
                barcode.ProductoSku.SaldosInventario
                    .Where(balance => balance.Ubicacion == UbicacionInventario.EXHIBICION)
                    .Sum(balance => balance.ExistenciaFisica - balance.Reservado),
                barcode.ProductoSku.SaldosInventario
                    .Where(balance => balance.Ubicacion == UbicacionInventario.BODEGA)
                    .Sum(balance => balance.ExistenciaFisica - balance.Reservado),
                barcode.ProductoSku.SaldosInventario
                    .Where(balance => balance.Ubicacion == UbicacionInventario.SIN_ASIGNAR)
                    .Sum(balance => balance.ExistenciaFisica - balance.Reservado)))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BarcodeSkuCandidate>> GetSkuCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var lease = await CreateContextAsync(cancellationToken);
        return await lease.Context.ProductoSkus
            .AsNoTracking()
            .Where(sku => sku.Activo && sku.Producto.Estado == EstadoProducto.ACTIVO)
            .OrderBy(sku => sku.Producto.Nombre)
            .ThenBy(sku => sku.CodigoSku)
            .Select(sku => new BarcodeSkuCandidate(
                sku.ProductoId,
                sku.ProductoSkuId,
                sku.Producto.CodigoInterno,
                sku.Producto.Nombre,
                sku.CodigoSku,
                sku.NombreVariante,
                sku.PrecioVentaOverrideCentavos ?? sku.Producto.PrecioVentaCentavos,
                sku.Barcodes
                    .Where(barcode => barcode.Activo)
                    .OrderByDescending(barcode => barcode.EsPrincipal)
                    .ThenBy(barcode => barcode.Codigo)
                    .Select(barcode => barcode.Codigo)
                    .FirstOrDefault(),
                sku.Barcodes.Any(barcode => !barcode.Activo)))
            .ToListAsync(cancellationToken);
    }

    public async Task<GenerateMissingBarcodesResult> GenerateMissingBarcodesAsync(
        GenerateMissingBarcodesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureGenerationAllowedAsync("Generar codigos de barras faltantes", cancellationToken);

        var requestedIds = request.ProductoSkuIds?
            .Where(id => id > 0)
            .Distinct()
            .ToHashSet();
        var candidates = await GetSkuCandidatesAsync(cancellationToken);
        var selected = requestedIds is null
            ? candidates
            : candidates.Where(candidate => requestedIds.Contains(candidate.ProductoSkuId)).ToList();
        var items = new List<MissingBarcodeGenerationItem>(selected.Count);

        foreach (var candidate in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate.TieneBarcodeActivo)
            {
                items.Add(new MissingBarcodeGenerationItem(candidate,
                    MissingBarcodeGenerationStatus.Omitted,
                    candidate.BarcodeActivo,
                    "El SKU ya conserva un código de barras activo."));
                continue;
            }

            // Un barcode inactivo es historial operacional. No se reactiva ni se reemplaza
            // durante una generación masiva; requiere una decisión explícita en la ficha SKU.
            if (candidate.TieneBarcodeHistoricoInactivo)
            {
                items.Add(new MissingBarcodeGenerationItem(candidate,
                    MissingBarcodeGenerationStatus.Omitted,
                    null,
                    "El SKU conserva un código histórico inactivo; no se modifica automáticamente."));
                continue;
            }

            items.Add(await GenerateMissingForSkuAsync(candidate, cancellationToken));
        }

        return new GenerateMissingBarcodesResult(items);
    }

    public async Task<ProductSkuBarcodeSummary> AssignAsync(
        AssignProductSkuBarcodeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureWriteAllowedAsync("Asignar codigo de barras", cancellationToken);
        var code = NormalizeRequiredCode(request.Codigo);
        return await CreateBarcodeAsync(
            request.ProductoSkuId,
            code,
            request.EsPrincipal,
            request.Symbology,
            generated: false,
            cancellationToken);
    }

    public async Task<ProductSkuBarcodeSummary> GenerateInternalAsync(
        GenerateProductSkuBarcodeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureGenerationAllowedAsync("Generar codigo de barras interno", cancellationToken);

        for (var attempt = 0; attempt < GenerationAttempts; attempt++)
        {
            var code = _codeGenerator();
            await using var lease = await CreateContextAsync(cancellationToken);
            var collision = await lease.Context.ProductoSkuBarcodes
                .AsNoTracking()
                .AnyAsync(barcode => barcode.Codigo == code, cancellationToken);
            if (collision)
            {
                continue;
            }

            return await CreateBarcodeAsync(
                request.ProductoSkuId,
                code,
                request.EsPrincipal,
                BarcodeSymbology.Code128,
                generated: true,
                cancellationToken);
        }

        throw new InvalidOperationException("No se pudo generar un codigo de barras interno unico.");
    }

    public async Task<ProductSkuBarcodeSummary> GenerateQrAsync(
        GenerateProductSkuQrRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureGenerationAllowedAsync("Generar código QR", cancellationToken);
        if (request.ProductoSkuId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        await using var lease = await CreateContextAsync(cancellationToken);
        var context = lease.Context;
        var sku = await context.ProductoSkus
            .Include(item => item.Barcodes)
            .SingleOrDefaultAsync(item => item.ProductoSkuId == request.ProductoSkuId, cancellationToken)
            ?? throw new InvalidOperationException("El SKU no existe.");
        var payload = BuildQrPayload(sku.CodigoSku);
        var existing = sku.Barcodes.SingleOrDefault(item =>
            item.Symbology == BarcodeSymbology.Qr && item.Codigo == payload);
        if (existing is not null)
        {
            return ToSummary(existing);
        }

        if (await context.ProductoSkuBarcodes.AsNoTracking()
                .AnyAsync(item => item.Codigo == payload, cancellationToken))
        {
            throw new InvalidOperationException("El payload QR ya está asignado a otro SKU.");
        }

        return await CreateBarcodeAsync(
            request.ProductoSkuId,
            payload,
            request.EsPrincipal,
            BarcodeSymbology.Qr,
            generated: true,
            cancellationToken);
    }

    public async Task SetPrincipalAsync(
        int productoSkuBarcodeId,
        CancellationToken cancellationToken = default)
    {
        await EnsureWriteAllowedAsync("Cambiar codigo de barras principal", cancellationToken);
        await using var lease = await CreateContextAsync(cancellationToken);
        var context = lease.Context;
        var barcode = await context.ProductoSkuBarcodes
            .Include(item => item.ProductoSku)
                .ThenInclude(sku => sku.Barcodes)
            .SingleOrDefaultAsync(
                item => item.ProductoSkuBarcodeId == productoSkuBarcodeId,
                cancellationToken)
            ?? throw new InvalidOperationException("El codigo de barras no existe.");

        if (!barcode.Activo)
        {
            throw new InvalidOperationException("Un codigo de barras inactivo no puede ser principal.");
        }

        if (barcode.EsPrincipal)
        {
            return;
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var previousPrincipals = barcode.ProductoSku.Barcodes
            .Where(current => current.EsPrincipal)
            .ToList();
        foreach (var current in previousPrincipals)
        {
            current.EsPrincipal = false;
        }
        if (previousPrincipals.Count > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        barcode.EsPrincipal = true;
        await context.SaveChangesAsync(cancellationToken);
        await AuditAsync(
            context,
            TipoBitacora.PRODUCTO_BARCODE_PRINCIPAL_MODIFICADO,
            barcode.ProductoSkuBarcodeId,
            $"El codigo '{barcode.Codigo}' se marco como principal para el SKU '{barcode.ProductoSku.CodigoSku}'.",
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SetActiveAsync(
        int productoSkuBarcodeId,
        bool active,
        CancellationToken cancellationToken = default)
    {
        await EnsureWriteAllowedAsync("Cambiar estado de codigo de barras", cancellationToken);
        await using var lease = await CreateContextAsync(cancellationToken);
        var context = lease.Context;
        var barcode = await context.ProductoSkuBarcodes
            .Include(item => item.ProductoSku)
            .SingleOrDefaultAsync(
                item => item.ProductoSkuBarcodeId == productoSkuBarcodeId,
                cancellationToken)
            ?? throw new InvalidOperationException("El codigo de barras no existe.");

        if (barcode.Activo == active)
        {
            return;
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var principalCleared = barcode.EsPrincipal && !active;
        barcode.Activo = active;
        if (!active)
        {
            barcode.EsPrincipal = false;
        }

        await context.SaveChangesAsync(cancellationToken);
        await AuditAsync(
            context,
            TipoBitacora.PRODUCTO_BARCODE_ESTADO_MODIFICADO,
            barcode.ProductoSkuBarcodeId,
            $"Codigo '{barcode.Codigo}' {(active ? "activado" : "desactivado")} para el SKU '{barcode.ProductoSku.CodigoSku}'.",
            cancellationToken);
        if (principalCleared)
        {
            await AuditAsync(
                context,
                TipoBitacora.PRODUCTO_BARCODE_PRINCIPAL_MODIFICADO,
                barcode.ProductoSkuBarcodeId,
                $"El codigo principal '{barcode.Codigo}' dejo de ser principal al desactivarse.",
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<ProductSkuBarcodeSummary> CreateBarcodeAsync(
        int productoSkuId,
        string code,
        bool primary,
        BarcodeSymbology symbology,
        bool generated,
        CancellationToken cancellationToken)
    {
        if (productoSkuId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(productoSkuId), "El SKU no es valido.");
        }
        if (!Enum.IsDefined(symbology))
        {
            throw new ArgumentOutOfRangeException(nameof(symbology));
        }

        await using var lease = await CreateContextAsync(cancellationToken);
        var context = lease.Context;
        if (await context.ProductoSkuBarcodes.AsNoTracking()
                .AnyAsync(barcode => barcode.Codigo == code, cancellationToken))
        {
            throw new InvalidOperationException("El codigo de barras ya esta asignado.");
        }

        var sku = await context.ProductoSkus
            .Include(item => item.Barcodes)
            .SingleOrDefaultAsync(item => item.ProductoSkuId == productoSkuId, cancellationToken)
            ?? throw new InvalidOperationException("El SKU no existe.");

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (primary)
        {
            var previousPrincipals = sku.Barcodes.Where(item => item.EsPrincipal).ToList();
            foreach (var current in previousPrincipals)
            {
                current.EsPrincipal = false;
            }
            if (previousPrincipals.Count > 0)
            {
                await context.SaveChangesAsync(cancellationToken);
            }
        }

        var barcode = new ProductoSkuBarcode
        {
            ProductoSku = sku,
            Codigo = code,
            Symbology = symbology,
            Activo = true,
            EsPrincipal = primary
        };
        context.ProductoSkuBarcodes.Add(barcode);
        await context.SaveChangesAsync(cancellationToken);

        await AuditAsync(
            context,
            generated
                ? TipoBitacora.PRODUCTO_BARCODE_GENERADO
                : TipoBitacora.PRODUCTO_BARCODE_ASIGNADO,
            barcode.ProductoSkuBarcodeId,
            generated
                ? $"Codigo interno '{barcode.Codigo}' generado para el SKU '{sku.CodigoSku}'."
                : $"Codigo '{barcode.Codigo}' asignado al SKU '{sku.CodigoSku}'.",
            cancellationToken);
        if (primary)
        {
            await AuditAsync(
                context,
                TipoBitacora.PRODUCTO_BARCODE_PRINCIPAL_MODIFICADO,
                barcode.ProductoSkuBarcodeId,
                $"El codigo '{barcode.Codigo}' se establecio como principal para el SKU '{sku.CodigoSku}'.",
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return ToSummary(barcode);
    }

    private async Task<MissingBarcodeGenerationItem> GenerateMissingForSkuAsync(
        BarcodeSkuCandidate requestedCandidate,
        CancellationToken cancellationToken)
    {
        for (var operationAttempt = 0; operationAttempt < BulkOperationAttempts; operationAttempt++)
        {
            try
            {
                await using var lease = await CreateContextAsync(cancellationToken);
                var context = lease.Context;
                var sku = await context.ProductoSkus
                    .Include(item => item.Producto)
                    .Include(item => item.Barcodes)
                    .SingleOrDefaultAsync(item => item.ProductoSkuId == requestedCandidate.ProductoSkuId, cancellationToken);

                if (sku is null || !sku.Activo || sku.Producto.Estado != EstadoProducto.ACTIVO)
                {
                    return new MissingBarcodeGenerationItem(requestedCandidate,
                        MissingBarcodeGenerationStatus.Omitted,
                        null,
                        "El SKU ya no está activo o no existe.");
                }

                var currentCandidate = ToCandidate(sku);
                if (currentCandidate.TieneBarcodeActivo)
                {
                    return new MissingBarcodeGenerationItem(currentCandidate,
                        MissingBarcodeGenerationStatus.Omitted,
                        currentCandidate.BarcodeActivo,
                        "Otro proceso o usuario ya dejó un código activo en este SKU.");
                }

                if (currentCandidate.TieneBarcodeHistoricoInactivo)
                {
                    return new MissingBarcodeGenerationItem(currentCandidate,
                        MissingBarcodeGenerationStatus.Omitted,
                        null,
                        "El SKU conserva un código histórico inactivo; no se modifica automáticamente.");
                }

                for (var codeAttempt = 0; codeAttempt < GenerationAttempts; codeAttempt++)
                {
                    var code = _codeGenerator();
                    var collision = await context.ProductoSkuBarcodes
                        .AsNoTracking()
                        .AnyAsync(barcode => barcode.Codigo == code, cancellationToken);
                    if (collision)
                    {
                        continue;
                    }

                    await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
                    var barcode = new ProductoSkuBarcode
                    {
                        ProductoSku = sku,
                        Codigo = code,
                        Activo = true,
                        EsPrincipal = true
                    };
                    context.ProductoSkuBarcodes.Add(barcode);
                    await context.SaveChangesAsync(cancellationToken);
                    await AuditAsync(
                        context,
                        TipoBitacora.PRODUCTO_BARCODE_GENERADO,
                        barcode.ProductoSkuBarcodeId,
                        $"Código interno '{barcode.Codigo}' generado masivamente para el SKU '{sku.CodigoSku}'.",
                        cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    return new MissingBarcodeGenerationItem(currentCandidate,
                        MissingBarcodeGenerationStatus.Generated,
                        barcode.Codigo,
                        "Código interno Code 128 generado.");
                }

                return new MissingBarcodeGenerationItem(currentCandidate,
                    MissingBarcodeGenerationStatus.Error,
                    null,
                    "No se pudo generar un código interno único después de varios intentos.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (DbUpdateException) when (operationAttempt + 1 < BulkOperationAttempts)
            {
                // La restricción única de SQLite es la autoridad final. Al reintentar,
                // el siguiente ciclo vuelve a leer el SKU y lo marcará omitido si otro proceso ganó.
            }
            catch (Exception ex) when (operationAttempt + 1 < BulkOperationAttempts && IsTransientWriteCollision(ex))
            {
                // SQLite puede informar una contención de escritura antes de llegar al índice único.
                // Se relee el SKU en el siguiente intento sin retener una transacción de lote.
            }
            catch (Exception ex)
            {
                return new MissingBarcodeGenerationItem(requestedCandidate,
                    MissingBarcodeGenerationStatus.Error,
                    null,
                    ToControlledError(ex));
            }
        }

        return new MissingBarcodeGenerationItem(requestedCandidate,
            MissingBarcodeGenerationStatus.Error,
            null,
            "No se pudo completar la generación por una colisión de escritura. Intente nuevamente.");
    }

    private async Task EnsureWriteAllowedAsync(
        string operation,
        CancellationToken cancellationToken)
    {
        _installation.EnsureOperationalWriteAllowed(operation);
        await _permissions.EnsurePermissionAsync(PermisoEmpleado.PRODUCTOS_GESTIONAR, cancellationToken);
    }

    private async Task EnsureGenerationAllowedAsync(
        string operation,
        CancellationToken cancellationToken)
    {
        _installation.EnsureOperationalWriteAllowed(operation);
        if (await _permissions.HasAnyPermissionAsync(
                [PermisoEmpleado.PRODUCTOS_GESTIONAR, PermisoEmpleado.ETIQUETAS_GESTIONAR],
                cancellationToken: cancellationToken))
        {
            return;
        }

        var definition = EmployeePermissionCatalog.GetDefinition(PermisoEmpleado.ETIQUETAS_GESTIONAR);
        throw new InvalidOperationException($"No tiene permiso para: {definition.Nombre}.");
    }

    private async Task AuditAsync(
        AppDbContext context,
        TipoBitacora type,
        int id,
        string description,
        CancellationToken cancellationToken)
    {
        var userId = _session.UsuarioActual?.UsuarioId
            ?? throw new InvalidOperationException("Debe existir una sesion activa.");
        var request = new AuditEntryRequest(
            userId,
            type,
            "producto_sku_barcodes",
            id,
            description);

        if (_contextFactory is null)
        {
            await _audit.RegistrarAsync(request, cancellationToken);
            return;
        }

        context.Bitacora.Add(AuditEntryFactory.Create(request));
        await context.SaveChangesAsync(cancellationToken);
    }

    private ValueTask<AppDbContextLease> CreateContextAsync(CancellationToken cancellationToken) =>
        AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);

    private static ProductSkuBarcodeSummary ToSummary(ProductoSkuBarcode barcode) =>
        new(
            barcode.ProductoSkuBarcodeId,
            barcode.ProductoSkuId,
            barcode.Codigo,
            barcode.Activo,
            barcode.EsPrincipal,
            barcode.Symbology);

    private static BarcodeSkuCandidate ToCandidate(ProductoSku sku)
    {
        var activeBarcode = sku.Barcodes
            .Where(item => item.Activo)
            .OrderByDescending(item => item.EsPrincipal)
            .ThenBy(item => item.Codigo, StringComparer.Ordinal)
            .Select(item => item.Codigo)
            .FirstOrDefault();
        return new BarcodeSkuCandidate(
            sku.ProductoId,
            sku.ProductoSkuId,
            sku.Producto.CodigoInterno,
            sku.Producto.Nombre,
            sku.CodigoSku,
            sku.NombreVariante,
            sku.PrecioVentaOverrideCentavos ?? sku.Producto.PrecioVentaCentavos,
            activeBarcode,
            sku.Barcodes.Any(item => !item.Activo));
    }

    private static string ToControlledError(Exception exception) => exception switch
    {
        DbUpdateException => "No se pudo guardar el código de barras.",
        InvalidOperationException => exception.Message,
        _ => "No se pudo generar el código de barras para este SKU."
    };

    private static bool IsTransientWriteCollision(Exception exception) => exception switch
    {
        SqliteException { SqliteErrorCode: 5 or 6 } => true,
        DbUpdateException { InnerException: SqliteException { SqliteErrorCode: 5 or 6 } } => true,
        _ => false
    };

    private static string? NormalizeLookupCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var code = value.Trim();
        return code.Length <= MaxCodeLength && !code.Any(char.IsControl)
            ? code
            : null;
    }

    private static string NormalizeRequiredCode(string? value)
    {
        var code = NormalizeLookupCode(value)
            ?? throw new ArgumentException(
                $"El codigo de barras es obligatorio, no puede contener controles ni superar {MaxCodeLength} caracteres.",
                nameof(value));
        return code;
    }

    private static string GenerateCode()
    {
        Span<char> digits = stackalloc char[10];
        for (var index = 0; index < digits.Length; index++)
        {
            digits[index] = (char)('0' + RandomNumberGenerator.GetInt32(0, 10));
        }

        return $"AI{new string(digits)}";
    }

    private static string BuildQrPayload(string skuCode)
    {
        if (string.IsNullOrWhiteSpace(skuCode) || skuCode.Any(char.IsControl))
        {
            throw new InvalidOperationException("El SKU no tiene un código válido para QR.");
        }

        return $"AI:SKU:{skuCode.Trim()}";
    }
}
