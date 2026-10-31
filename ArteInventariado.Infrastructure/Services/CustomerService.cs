using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Entities;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class CustomerService : ICustomerService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICurrentSession _currentSession;
    private readonly IEmployeePermissionService _permissionService;
    private readonly IAuditService _auditService;

    [ActivatorUtilitiesConstructor]
    public CustomerService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentSession currentSession,
        IEmployeePermissionService permissionService,
        IAuditService auditService)
    {
        _contextFactory = contextFactory;
        _currentSession = currentSession;
        _permissionService = permissionService;
        _auditService = auditService;
    }

    internal CustomerService(AppDbContext context, ICurrentSession currentSession, IEmployeePermissionService permissionService, IAuditService auditService)
    {
        _legacyContext = context;
        _currentSession = currentSession;
        _permissionService = permissionService;
        _auditService = auditService;
    }

    private AppDbContext Context => AppDbContextLease.Current ?? _legacyContext
        ?? throw new InvalidOperationException("No existe un contexto activo para la operacion de cliente.");

    public async Task<CustomerDetail> CreateAsync(
        CustomerSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CLIENTE_CREAR, cancellationToken);
        ValidateRequest(request);

        var duplicates = await FindDuplicatesInternalAsync(request, cancellationToken);
        EnsureDuplicatesCanProceed(duplicates, request.ConfirmDuplicateWarning);

        var usuarioId = GetCurrentUserId();
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var cliente = new Cliente
        {
            CodigoCliente = await GenerateCustomerCodeAsync(cancellationToken),
            NombreCompleto = request.NombreCompleto.Trim(),
            NombreNormalizado = CustomerTextNormalizer.NormalizeName(request.NombreCompleto),
            TelefonoCelular = NormalizeOptional(request.TelefonoCelular),
            TelefonoNormalizado = CustomerTextNormalizer.NormalizePhone(request.TelefonoCelular),
            WhatsApp = NormalizeOptional(request.WhatsApp),
            CorreoElectronico = NormalizeOptional(request.CorreoElectronico),
            CorreoNormalizado = CustomerTextNormalizer.NormalizeEmail(request.CorreoElectronico),
            Direccion = NormalizeOptional(request.Direccion),
            DocumentoIdentidad = NormalizeOptional(request.DocumentoIdentidad),
            DocumentoNormalizado = CustomerTextNormalizer.NormalizeDocument(request.DocumentoIdentidad),
            Notas = NormalizeOptional(request.Notas),
            Estado = EstadoCliente.ACTIVO,
            FechaCreacion = now,
            FechaActualizacion = now,
            UsuarioCreacionId = usuarioId,
            UsuarioActualizacionId = usuarioId
        };

        Context.Clientes.Add(cliente);
        await Context.SaveChangesAsync(cancellationToken);

        await _auditService.RegistrarAsync(
            new AuditEntryRequest(
                usuarioId,
                TipoBitacora.CLIENTE_CREADO,
                "clientes",
                cliente.ClienteId,
                $"Cliente {cliente.CodigoCliente} creado.",
                DatosDespues: BuildAuditSnapshot(cliente)),
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return (await GetDetailInternalAsync(cliente.ClienteId, cancellationToken))!;
    }

    public async Task<CustomerDetail> UpdateAsync(
        CustomerSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CLIENTE_EDITAR, cancellationToken);
        ValidateRequest(request);

        if (request.ClienteId is null)
        {
            throw new ArgumentException("El cliente a editar es obligatorio.", nameof(request));
        }

        var cliente = await Context.Clientes
            .FirstOrDefaultAsync(c => c.ClienteId == request.ClienteId.Value, cancellationToken)
            ?? throw new InvalidOperationException("El cliente no existe.");

        var duplicates = await FindDuplicatesInternalAsync(request, cancellationToken);
        EnsureDuplicatesCanProceed(duplicates, request.ConfirmDuplicateWarning);

        var before = BuildAuditSnapshot(cliente);
        var usuarioId = GetCurrentUserId();
        var newValues = BuildNormalizedValues(request);
        var changed =
            cliente.NombreCompleto != newValues.NombreCompleto ||
            cliente.NombreNormalizado != newValues.NombreNormalizado ||
            cliente.TelefonoCelular != newValues.TelefonoCelular ||
            cliente.TelefonoNormalizado != newValues.TelefonoNormalizado ||
            cliente.WhatsApp != newValues.WhatsApp ||
            cliente.CorreoElectronico != newValues.CorreoElectronico ||
            cliente.CorreoNormalizado != newValues.CorreoNormalizado ||
            cliente.Direccion != newValues.Direccion ||
            cliente.DocumentoIdentidad != newValues.DocumentoIdentidad ||
            cliente.DocumentoNormalizado != newValues.DocumentoNormalizado ||
            cliente.Notas != newValues.Notas;

        if (!changed)
        {
            return (await GetDetailInternalAsync(cliente.ClienteId, cancellationToken))!;
        }

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        cliente.NombreCompleto = newValues.NombreCompleto;
        cliente.NombreNormalizado = newValues.NombreNormalizado;
        cliente.TelefonoCelular = newValues.TelefonoCelular;
        cliente.TelefonoNormalizado = newValues.TelefonoNormalizado;
        cliente.WhatsApp = newValues.WhatsApp;
        cliente.CorreoElectronico = newValues.CorreoElectronico;
        cliente.CorreoNormalizado = newValues.CorreoNormalizado;
        cliente.Direccion = newValues.Direccion;
        cliente.DocumentoIdentidad = newValues.DocumentoIdentidad;
        cliente.DocumentoNormalizado = newValues.DocumentoNormalizado;
        cliente.Notas = newValues.Notas;
        cliente.FechaActualizacion = DateTime.UtcNow;
        cliente.UsuarioActualizacionId = usuarioId;

        await Context.SaveChangesAsync(cancellationToken);

        await _auditService.RegistrarAsync(
            new AuditEntryRequest(
                usuarioId,
                TipoBitacora.CLIENTE_EDITADO,
                "clientes",
                cliente.ClienteId,
                $"Cliente {cliente.CodigoCliente} editado.",
                DatosAntes: before,
                DatosDespues: BuildAuditSnapshot(cliente)),
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return (await GetDetailInternalAsync(cliente.ClienteId, cancellationToken))!;
    }

    public async Task SetStateAsync(
        CustomerStateChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await EnsureCurrentAdminAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Motivo))
        {
            throw new ArgumentException("El motivo es obligatorio.", nameof(request));
        }

        var cliente = await Context.Clientes
            .FirstOrDefaultAsync(c => c.ClienteId == request.ClienteId, cancellationToken)
            ?? throw new InvalidOperationException("El cliente no existe.");

        if (cliente.Estado == request.Estado)
        {
            return;
        }

        var usuarioId = GetCurrentUserId();
        var previous = cliente.Estado;
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        cliente.Estado = request.Estado;
        cliente.FechaActualizacion = DateTime.UtcNow;
        cliente.UsuarioActualizacionId = usuarioId;
        await Context.SaveChangesAsync(cancellationToken);

        await _auditService.RegistrarAsync(
            new AuditEntryRequest(
                usuarioId,
                TipoBitacora.CLIENTE_ESTADO_MODIFICADO,
                "clientes",
                cliente.ClienteId,
                $"Cliente {cliente.CodigoCliente} cambio de {previous} a {request.Estado}. Motivo: {request.Motivo.Trim()}",
                DatosAntes: $"Estado={previous}",
                DatosDespues: $"Estado={request.Estado}"),
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<CustomerDuplicateCheckResult> FindDuplicatesAsync(
        CustomerSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(_contextFactory, _legacyContext, cancellationToken);
        await _permissionService.EnsurePermissionAsync(PermisoEmpleado.CLIENTES_ACCEDER, cancellationToken);
        ValidateRequest(request);
        return await FindDuplicatesInternalAsync(request, cancellationToken);
    }

    private async Task<CustomerDuplicateCheckResult> FindDuplicatesInternalAsync(
        CustomerSaveRequest request,
        CancellationToken cancellationToken)
    {
        var values = BuildNormalizedValues(request);
        var excludedId = request.ClienteId;
        var candidates = await Context.Clientes
            .AsNoTracking()
            .Where(c => excludedId == null || c.ClienteId != excludedId.Value)
            .ToListAsync(cancellationToken);

        var duplicates = new List<CustomerDuplicateCandidate>();
        foreach (var customer in candidates)
        {
            if (!string.IsNullOrWhiteSpace(values.DocumentoNormalizado) &&
                customer.DocumentoNormalizado == values.DocumentoNormalizado)
            {
                duplicates.Add(ToDuplicate(customer, CustomerDuplicateKind.Documento, blocking: true));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(values.TelefonoNormalizado) &&
                customer.TelefonoNormalizado == values.TelefonoNormalizado)
            {
                duplicates.Add(ToDuplicate(customer, CustomerDuplicateKind.Telefono, blocking: false));
            }

            if (!string.IsNullOrWhiteSpace(values.CorreoNormalizado) &&
                customer.CorreoNormalizado == values.CorreoNormalizado)
            {
                duplicates.Add(ToDuplicate(customer, CustomerDuplicateKind.Correo, blocking: false));
            }

            if (LooksLikeSimilarName(values.NombreNormalizado, customer.NombreNormalizado))
            {
                duplicates.Add(ToDuplicate(customer, CustomerDuplicateKind.NombreParecido, blocking: false));
            }
        }

        return new CustomerDuplicateCheckResult(
            duplicates
                .GroupBy(d => new { d.ClienteId, d.Kind })
                .Select(g => g.First())
                .OrderByDescending(d => d.Blocking)
                .ThenBy(d => d.NombreCompleto)
                .ToList());
    }

    private static void EnsureDuplicatesCanProceed(
        CustomerDuplicateCheckResult duplicates,
        bool confirmedWarning)
    {
        if (duplicates.HasBlockingDuplicate)
        {
            throw new InvalidOperationException("Ya existe un cliente con el mismo DUI o documento de identidad.");
        }

        if (duplicates.HasWarnings && !confirmedWarning)
        {
            throw new InvalidOperationException("Existen posibles clientes duplicados. Revise y confirme para continuar.");
        }
    }

    private static CustomerDuplicateCandidate ToDuplicate(
        Cliente customer,
        CustomerDuplicateKind kind,
        bool blocking)
    {
        return new CustomerDuplicateCandidate(
            customer.ClienteId,
            customer.CodigoCliente,
            customer.NombreCompleto,
            customer.TelefonoCelular,
            customer.DocumentoIdentidad,
            customer.Estado,
            kind,
            blocking);
    }

    private async Task<string> GenerateCustomerCodeAsync(CancellationToken cancellationToken)
    {
        var count = await Context.Clientes.CountAsync(cancellationToken);
        var next = count + 1;
        while (true)
        {
            var code = $"CLI-{next:000000}";
            if (!await Context.Clientes.AnyAsync(c => c.CodigoCliente == code, cancellationToken))
            {
                return code;
            }

            next++;
        }
    }

    private async Task<CustomerDetail?> GetDetailInternalAsync(
        int clienteId,
        CancellationToken cancellationToken)
    {
        return await Context.Clientes
            .AsNoTracking()
            .Include(c => c.UsuarioCreacion)
            .Include(c => c.UsuarioActualizacion)
            .Where(c => c.ClienteId == clienteId)
            .Select(c => new CustomerDetail(
                c.ClienteId,
                c.CodigoCliente,
                c.NombreCompleto,
                c.TelefonoCelular,
                c.WhatsApp,
                c.CorreoElectronico,
                c.Direccion,
                c.DocumentoIdentidad,
                c.Notas,
                c.Estado,
                c.FechaCreacion,
                c.FechaActualizacion,
                c.UsuarioCreacion.NombreUsuario,
                c.UsuarioActualizacion == null ? null : c.UsuarioActualizacion.NombreUsuario))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task EnsureCurrentAdminAsync(CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        var isAdmin = await Context.Usuarios
            .AsNoTracking()
            .AnyAsync(u => u.UsuarioId == currentUserId && u.Activo && u.Rol == RolUsuario.ADMIN, cancellationToken);

        if (!isAdmin)
        {
            throw new InvalidOperationException("Solo ADMIN puede activar o desactivar clientes.");
        }
    }

    private int GetCurrentUserId()
    {
        return _currentSession.UsuarioActual?.UsuarioId
            ?? throw new InvalidOperationException("Debe existir una sesion activa.");
    }

    private static void ValidateRequest(CustomerSaveRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NombreCompleto))
        {
            throw new ArgumentException("El nombre completo es obligatorio.", nameof(request));
        }

        if (request.NombreCompleto.Trim().Length > 180)
        {
            throw new ArgumentException("El nombre completo es demasiado largo.", nameof(request));
        }

        ValidateLength(request.TelefonoCelular, 40, "telefono");
        ValidateLength(request.WhatsApp, 40, "WhatsApp");
        ValidateLength(request.CorreoElectronico, 180, "correo");
        ValidateLength(request.Direccion, 500, "direccion");
        ValidateLength(request.DocumentoIdentidad, 40, "documento");
        ValidateLength(request.Notas, 1000, "notas");

        if (!string.IsNullOrWhiteSpace(request.CorreoElectronico) &&
            !request.CorreoElectronico.Contains('@'))
        {
            throw new ArgumentException("El correo electronico no tiene un formato valido.", nameof(request));
        }
    }

    private static void ValidateLength(string? value, int maxLength, string fieldName)
    {
        if (!string.IsNullOrWhiteSpace(value) && value.Trim().Length > maxLength)
        {
            throw new ArgumentException($"El campo {fieldName} es demasiado largo.");
        }
    }

    private static CustomerNormalizedValues BuildNormalizedValues(CustomerSaveRequest request)
    {
        return new CustomerNormalizedValues(
            request.NombreCompleto.Trim(),
            CustomerTextNormalizer.NormalizeName(request.NombreCompleto),
            NormalizeOptional(request.TelefonoCelular),
            CustomerTextNormalizer.NormalizePhone(request.TelefonoCelular),
            NormalizeOptional(request.WhatsApp),
            NormalizeOptional(request.CorreoElectronico),
            CustomerTextNormalizer.NormalizeEmail(request.CorreoElectronico),
            NormalizeOptional(request.Direccion),
            NormalizeOptional(request.DocumentoIdentidad),
            CustomerTextNormalizer.NormalizeDocument(request.DocumentoIdentidad),
            NormalizeOptional(request.Notas));
    }

    private static bool LooksLikeSimilarName(string candidate, string existing)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(existing))
        {
            return false;
        }

        if (candidate == existing)
        {
            return true;
        }

        var candidateTokens = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var existingTokens = existing.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (candidateTokens.Count == 0 || existingTokens.Count == 0)
        {
            return false;
        }

        var shared = candidateTokens.Intersect(existingTokens).Count();
        var ratio = shared / (double)Math.Max(candidateTokens.Count, existingTokens.Count);
        return shared >= 2 && ratio >= 0.67;
    }

    private static string BuildAuditSnapshot(Cliente cliente)
    {
        return string.Join("; ", [
            $"Codigo={cliente.CodigoCliente}",
            $"Nombre={cliente.NombreCompleto}",
            $"Telefono={cliente.TelefonoCelular}",
            $"Correo={cliente.CorreoElectronico}",
            $"Documento={cliente.DocumentoIdentidad}",
            $"Estado={cliente.Estado}"
        ]);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record CustomerNormalizedValues(
        string NombreCompleto,
        string NombreNormalizado,
        string? TelefonoCelular,
        string? TelefonoNormalizado,
        string? WhatsApp,
        string? CorreoElectronico,
        string? CorreoNormalizado,
        string? Direccion,
        string? DocumentoIdentidad,
        string? DocumentoNormalizado,
        string? Notas);
}
