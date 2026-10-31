using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class QuotationService : IQuotationService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _session;
    private readonly IEmployeePermissionService _permissions;
    private readonly IAuditService _audit;
    private readonly IQuotationQueryService _query;

    [ActivatorUtilitiesConstructor]
    public QuotationService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession session,
        IEmployeePermissionService permissions,
        IAuditService audit,
        IQuotationQueryService query)
    {
        _contextFactory = contextFactory;
        _session = session;
        _permissions = permissions;
        _audit = audit;
        _query = query;
    }

    internal QuotationService(AppDbContext context, ICurrentSession session, IEmployeePermissionService permissions, IAuditService audit, IQuotationQueryService query)
    {
        _legacyContext = context;
        _session = session;
        _permissions = permissions;
        _audit = audit;
        _query = query;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para la operacion de cotizacion.");

    public async Task<QuotationDetail> CreateDraftAsync(
        QuotationSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissions.EnsurePermissionAsync(PermisoEmpleado.COTIZACION_CREAR, cancellationToken);
        var userId = CurrentUserId();
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        var draft = await BuildDraftAsync(request, cancellationToken);
        var now = DateTime.UtcNow;
        var quotation = new Cotizacion
        {
            CodigoCotizacion = $"TMP-{Guid.NewGuid():N}"[..30],
            ClienteId = draft.ClienteId,
            NombreDestinatarioSnapshot = draft.Nombre,
            TelefonoSnapshot = draft.Telefono,
            CorreoSnapshot = draft.Correo,
            DireccionSnapshot = draft.Direccion,
            Fecha = request.Fecha.Date,
            FechaVencimiento = request.FechaVencimiento.Date,
            Estado = EstadoCotizacion.BORRADOR,
            SubtotalCentavos = draft.Subtotal,
            DescuentoGeneralCentavos = request.DescuentoGeneralCentavos,
            TotalCentavos = draft.Subtotal - request.DescuentoGeneralCentavos,
            CondicionesComerciales = Normalize(request.CondicionesComerciales),
            Observaciones = Normalize(request.Observaciones),
            UsuarioCreadorId = userId,
            UsuarioUltimaActualizacionId = userId,
            FechaCreacion = now,
            FechaActualizacion = now
        };
        foreach (var line in draft.Lines)
        {
            quotation.Detalles.Add(line);
        }

        Context.Cotizaciones.Add(quotation);
        await Context.SaveChangesAsync(cancellationToken);
        quotation.CodigoCotizacion = $"COT-{quotation.CotizacionId:000000}";
        quotation.HistorialEstados.Add(NewHistory(
            quotation.CotizacionId,
            null,
            EstadoCotizacion.BORRADOR,
            "Borrador creado.",
            userId));
        await Context.SaveChangesAsync(cancellationToken);

        await _audit.RegistrarAsync(new AuditEntryRequest(
            userId,
            TipoBitacora.COTIZACION_CREADA,
            "cotizaciones",
            quotation.CotizacionId,
            $"Cotizacion {quotation.CodigoCotizacion} creada como borrador.",
            DatosDespues: AuditSnapshot(quotation)), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await _query.GetByIdAsync(quotation.CotizacionId, cancellationToken))!;
    }

    public async Task<QuotationDetail> UpdateDraftAsync(
        QuotationSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissions.EnsurePermissionAsync(PermisoEmpleado.COTIZACION_EDITAR, cancellationToken);
        if (!request.CotizacionId.HasValue)
        {
            throw new ArgumentException("La cotizacion es obligatoria.");
        }

        var quotation = await Context.Cotizaciones
            .Include(c => c.Detalles)
            .FirstOrDefaultAsync(c => c.CotizacionId == request.CotizacionId.Value, cancellationToken)
            ?? throw new InvalidOperationException("La cotizacion no existe.");
        if (quotation.Estado != EstadoCotizacion.BORRADOR)
        {
            throw new InvalidOperationException("Solo se pueden editar cotizaciones en borrador.");
        }

        var userId = CurrentUserId();
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        var draft = await BuildDraftAsync(request, cancellationToken);
        var before = AuditSnapshot(quotation);
        quotation.ClienteId = draft.ClienteId;
        quotation.NombreDestinatarioSnapshot = draft.Nombre;
        quotation.TelefonoSnapshot = draft.Telefono;
        quotation.CorreoSnapshot = draft.Correo;
        quotation.DireccionSnapshot = draft.Direccion;
        quotation.Fecha = request.Fecha.Date;
        quotation.FechaVencimiento = request.FechaVencimiento.Date;
        quotation.SubtotalCentavos = draft.Subtotal;
        quotation.DescuentoGeneralCentavos = request.DescuentoGeneralCentavos;
        quotation.TotalCentavos = draft.Subtotal - request.DescuentoGeneralCentavos;
        quotation.CondicionesComerciales = Normalize(request.CondicionesComerciales);
        quotation.Observaciones = Normalize(request.Observaciones);
        quotation.UsuarioUltimaActualizacionId = userId;
        quotation.FechaActualizacion = DateTime.UtcNow;
        Context.DetalleCotizaciones.RemoveRange(quotation.Detalles);
        quotation.Detalles.Clear();
        foreach (var line in draft.Lines)
        {
            quotation.Detalles.Add(line);
        }

        await Context.SaveChangesAsync(cancellationToken);
        await _audit.RegistrarAsync(new AuditEntryRequest(
            userId,
            TipoBitacora.COTIZACION_EDITADA,
            "cotizaciones",
            quotation.CotizacionId,
            $"Cotizacion {quotation.CodigoCotizacion} editada.",
            before,
            AuditSnapshot(quotation)), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await _query.GetByIdAsync(quotation.CotizacionId, cancellationToken))!;
    }

    public async Task<QuotationDetail> EmitAsync(
        int cotizacionId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissions.EnsurePermissionAsync(PermisoEmpleado.COTIZACION_EMITIR, cancellationToken);
        return await ChangeStateInternalAsync(
            cotizacionId,
            EstadoCotizacion.EMITIDA,
            "Cotizacion emitida.",
            TipoBitacora.COTIZACION_EMITIDA,
            cancellationToken);
    }

    public async Task<QuotationDetail> ChangeStateAsync(
        QuotationStateChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        if (request.Estado == EstadoCotizacion.CANCELADA)
        {
            await _permissions.EnsurePermissionAsync(PermisoEmpleado.COTIZACION_CANCELAR, cancellationToken);
            if (string.IsNullOrWhiteSpace(request.Motivo))
            {
                throw new ArgumentException("El motivo de cancelacion es obligatorio.");
            }
        }
        else
        {
            await _permissions.EnsurePermissionAsync(PermisoEmpleado.COTIZACION_EMITIR, cancellationToken);
        }

        return await ChangeStateInternalAsync(
            request.CotizacionId,
            request.Estado,
            request.Motivo,
            TipoBitacora.COTIZACION_ESTADO_MODIFICADO,
            cancellationToken);
    }

    public async Task<QuotationDetail> DuplicateAsync(
        int cotizacionId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissions.EnsurePermissionAsync(PermisoEmpleado.COTIZACION_CREAR, cancellationToken);
        var userId = CurrentUserId();
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        int copyId;
        try
        {
            var source = await _query.GetByIdAsync(cotizacionId, cancellationToken)
                ?? throw new InvalidOperationException("La cotizacion no existe.");
            var now = DateTime.UtcNow;
            var date = DateTime.Today;
            var copy = new Cotizacion
            {
                CodigoCotizacion = $"TMP-{Guid.NewGuid():N}"[..30],
                ClienteId = source.ClienteId,
                NombreDestinatarioSnapshot = source.Destinatario,
                TelefonoSnapshot = source.Telefono,
                CorreoSnapshot = source.Correo,
                DireccionSnapshot = source.Direccion,
                Fecha = date,
                FechaVencimiento = date.AddDays(Math.Max(1, (source.FechaVencimiento.Date - source.Fecha.Date).Days)),
                Estado = EstadoCotizacion.BORRADOR,
                SubtotalCentavos = source.SubtotalCentavos,
                DescuentoGeneralCentavos = source.DescuentoGeneralCentavos,
                TotalCentavos = source.TotalCentavos,
                CondicionesComerciales = source.CondicionesComerciales,
                Observaciones = source.Observaciones,
                UsuarioCreadorId = userId,
                UsuarioUltimaActualizacionId = userId,
                FechaCreacion = now,
                FechaActualizacion = now
            };
            foreach (var line in source.Detalles)
            {
                copy.Detalles.Add(new DetalleCotizacion
                {
                    ProductoId = line.ProductoId,
                    CodigoProductoSnapshot = line.Codigo,
                    NombreDescripcionSnapshot = line.Descripcion,
                    Cantidad = line.Cantidad,
                    PrecioUnitarioCentavos = line.PrecioUnitarioCentavos,
                    DescuentoLineaCentavos = line.DescuentoLineaCentavos,
                    SubtotalCentavos = line.SubtotalCentavos,
                    Observaciones = line.Observaciones
                });
            }

            Context.Cotizaciones.Add(copy);
            await Context.SaveChangesAsync(cancellationToken);
            copy.CodigoCotizacion = $"COT-{copy.CotizacionId:000000}";
            copy.HistorialEstados.Add(NewHistory(
                copy.CotizacionId,
                null,
                EstadoCotizacion.BORRADOR,
                $"Borrador duplicado desde {source.CodigoCotizacion}.",
                userId));
            await Context.SaveChangesAsync(cancellationToken);

            await _audit.RegistrarAsync(new AuditEntryRequest(
                userId,
                TipoBitacora.COTIZACION_DUPLICADA,
                "cotizaciones",
                copy.CotizacionId,
                $"Cotizacion {source.CodigoCotizacion} duplicada como {copy.CodigoCotizacion}.",
                DatosDespues: AuditSnapshot(copy)), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            copyId = copy.CotizacionId;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            Context.ChangeTracker.Clear();
            throw;
        }

        return (await _query.GetByIdAsync(copyId, cancellationToken))!;
    }

    public async Task<int> UpdateExpiredAsync(CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissions.EnsurePermissionAsync(PermisoEmpleado.COTIZACIONES_ACCEDER, cancellationToken);
        var today = DateTime.Today;
        var userId = CurrentUserId();
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        var expired = await Context.Cotizaciones
            .Where(c => c.Estado == EstadoCotizacion.EMITIDA && c.FechaVencimiento < today)
            .ToListAsync(cancellationToken);
        foreach (var quotation in expired)
        {
            quotation.Estado = EstadoCotizacion.VENCIDA;
            quotation.FechaCierre = DateTime.UtcNow;
            quotation.FechaActualizacion = DateTime.UtcNow;
            quotation.UsuarioUltimaActualizacionId = userId;
            quotation.HistorialEstados.Add(NewHistory(
                quotation.CotizacionId,
                EstadoCotizacion.EMITIDA,
                EstadoCotizacion.VENCIDA,
                "Vigencia finalizada automaticamente.",
                userId));
        }

        await Context.SaveChangesAsync(cancellationToken);
        foreach (var quotation in expired)
        {
            await _audit.RegistrarAsync(new AuditEntryRequest(
                userId,
                TipoBitacora.COTIZACION_ESTADO_MODIFICADO,
                "cotizaciones",
                quotation.CotizacionId,
                $"Cotizacion {quotation.CodigoCotizacion}: EMITIDA -> VENCIDA. Vigencia finalizada automaticamente."),
                cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return expired.Count;
    }

    private async Task<QuotationDetail> ChangeStateInternalAsync(
        int id,
        EstadoCotizacion target,
        string? reason,
        TipoBitacora auditType,
        CancellationToken cancellationToken)
    {
        if (target == EstadoCotizacion.CONVERTIDA)
        {
            throw new InvalidOperationException("La conversion queda reservada para un bloque futuro.");
        }

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        var quotation = await Context.Cotizaciones
            .Include(c => c.HistorialEstados)
            .FirstOrDefaultAsync(c => c.CotizacionId == id, cancellationToken)
            ?? throw new InvalidOperationException("La cotizacion no existe.");
        ValidateTransition(quotation.Estado, target);
        var userId = CurrentUserId();
        var previous = quotation.Estado;
        var now = DateTime.UtcNow;
        quotation.Estado = target;
        quotation.UsuarioUltimaActualizacionId = userId;
        quotation.FechaActualizacion = now;
        quotation.FechaEmision = target == EstadoCotizacion.EMITIDA ? now : quotation.FechaEmision;
        quotation.FechaCierre = target is EstadoCotizacion.ACEPTADA or EstadoCotizacion.RECHAZADA
            or EstadoCotizacion.VENCIDA or EstadoCotizacion.CANCELADA ? now : null;
        quotation.MotivoCancelacion = target == EstadoCotizacion.CANCELADA ? reason!.Trim() : quotation.MotivoCancelacion;
        quotation.HistorialEstados.Add(NewHistory(id, previous, target, Normalize(reason), userId));
        await Context.SaveChangesAsync(cancellationToken);
        await _audit.RegistrarAsync(new AuditEntryRequest(
            userId,
            auditType,
            "cotizaciones",
            id,
            $"Cotizacion {quotation.CodigoCotizacion}: {previous} -> {target}. {Normalize(reason)}"), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await _query.GetByIdAsync(id, cancellationToken))!;
    }

    private async Task<QuotationDraft> BuildDraftAsync(
        QuotationSaveRequest request,
        CancellationToken cancellationToken)
    {
        if (request.FechaVencimiento.Date < request.Fecha.Date)
        {
            throw new ArgumentException("La fecha de vencimiento no puede ser anterior a la fecha.");
        }
        if (request.Detalles.Count == 0)
        {
            throw new ArgumentException("La cotizacion debe tener al menos una linea.");
        }

        Cliente? client = null;
        if (request.ClienteId.HasValue)
        {
            client = await Context.Clientes
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.ClienteId == request.ClienteId.Value, cancellationToken)
                ?? throw new InvalidOperationException("El cliente no existe.");
            if (client.Estado != EstadoCliente.ACTIVO)
            {
                throw new InvalidOperationException("No se puede cotizar a un cliente inactivo.");
            }
        }

        var name = Normalize(request.NombreDestinatario) ?? client?.NombreCompleto;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("El nombre del destinatario es obligatorio.");
        }

        var productIds = request.Detalles
            .Where(d => d.ProductoId.HasValue)
            .Select(d => d.ProductoId!.Value)
            .Distinct()
            .ToList();
        var products = await Context.Productos
            .AsNoTracking()
            .Where(p => productIds.Contains(p.ProductoId))
            .ToDictionaryAsync(p => p.ProductoId, cancellationToken);
        if (products.Count != productIds.Count)
        {
            throw new InvalidOperationException("Uno de los productos no existe.");
        }

        var lines = new List<DetalleCotizacion>();
        foreach (var input in request.Detalles)
        {
            if (input.Cantidad <= 0) throw new ArgumentException("La cantidad debe ser mayor que cero.");
            if (input.PrecioUnitarioCentavos < 0) throw new ArgumentException("El precio unitario no puede ser negativo.");
            if (input.DescuentoLineaCentavos < 0) throw new ArgumentException("El descuento de linea no puede ser negativo.");
            var gross = checked(input.Cantidad * input.PrecioUnitarioCentavos);
            if (input.DescuentoLineaCentavos > gross)
            {
                throw new ArgumentException("El descuento de linea no puede superar el importe de la linea.");
            }

            Producto? product = null;
            if (input.ProductoId.HasValue)
            {
                product = products[input.ProductoId.Value];
                if (product.Estado != EstadoProducto.ACTIVO)
                {
                    throw new InvalidOperationException($"El producto '{product.Nombre}' no esta activo.");
                }
            }
            var description = Normalize(input.Descripcion) ?? product?.Nombre;
            if (string.IsNullOrWhiteSpace(description))
            {
                throw new ArgumentException("La descripcion de la linea es obligatoria.");
            }
            lines.Add(new DetalleCotizacion
            {
                ProductoId = product?.ProductoId,
                CodigoProductoSnapshot = product?.CodigoInterno,
                NombreDescripcionSnapshot = description,
                Cantidad = input.Cantidad,
                PrecioUnitarioCentavos = input.PrecioUnitarioCentavos,
                DescuentoLineaCentavos = input.DescuentoLineaCentavos,
                SubtotalCentavos = gross - input.DescuentoLineaCentavos,
                Observaciones = Normalize(input.Observaciones)
            });
        }

        var subtotal = lines.Sum(l => l.SubtotalCentavos);
        if (request.DescuentoGeneralCentavos < 0 || request.DescuentoGeneralCentavos > subtotal)
        {
            throw new ArgumentException("El descuento general debe estar entre cero y el subtotal.");
        }
        return new QuotationDraft(
            client?.ClienteId,
            name,
            Normalize(request.Telefono) ?? client?.TelefonoCelular,
            Normalize(request.Correo) ?? client?.CorreoElectronico,
            Normalize(request.Direccion) ?? client?.Direccion,
            subtotal,
            lines);
    }

    private static void ValidateTransition(EstadoCotizacion current, EstadoCotizacion target)
    {
        var allowed = current switch
        {
            EstadoCotizacion.BORRADOR => target is EstadoCotizacion.EMITIDA or EstadoCotizacion.CANCELADA,
            EstadoCotizacion.EMITIDA => target is EstadoCotizacion.ACEPTADA or EstadoCotizacion.RECHAZADA
                or EstadoCotizacion.VENCIDA or EstadoCotizacion.CANCELADA,
            _ => false
        };
        if (!allowed)
        {
            throw new InvalidOperationException($"No se puede cambiar una cotizacion de {current} a {target}.");
        }
    }

    private int CurrentUserId() => _session.UsuarioActual?.UsuarioId
        ?? throw new InvalidOperationException("Debe existir una sesion activa.");
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static HistorialEstadoCotizacion NewHistory(
        int quotationId,
        EstadoCotizacion? previous,
        EstadoCotizacion target,
        string? reason,
        int userId) => new()
        {
            CotizacionId = quotationId,
            EstadoAnterior = previous,
            EstadoNuevo = target,
            Motivo = reason,
            UsuarioId = userId,
            Fecha = DateTime.UtcNow
        };
    private static string AuditSnapshot(Cotizacion c) =>
        $"Codigo={c.CodigoCotizacion}; ClienteId={c.ClienteId}; Destinatario={c.NombreDestinatarioSnapshot}; " +
        $"Estado={c.Estado}; Subtotal={c.SubtotalCentavos}; Descuento={c.DescuentoGeneralCentavos}; Total={c.TotalCentavos}; Lineas={c.Detalles.Count}";
    private sealed record QuotationDraft(
        int? ClienteId,
        string Nombre,
        string? Telefono,
        string? Correo,
        string? Direccion,
        long Subtotal,
        IReadOnlyList<DetalleCotizacion> Lines);
}
