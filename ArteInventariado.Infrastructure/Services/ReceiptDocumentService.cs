using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Services;

internal sealed class ReceiptDocumentService : IReceiptDocumentService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ISalesQueryService _sales;
    private readonly IEmployeePermissionService _permissions;
    private readonly ICurrentSession _session;
    private readonly IInstallationContext _installation;
    private readonly IAuditService _audit;

    public ReceiptDocumentService(
        IDbContextFactory<AppDbContext> contextFactory,
        ISalesQueryService sales,
        IEmployeePermissionService permissions,
        ICurrentSession session,
        IInstallationContext installation,
        IAuditService audit)
    {
        _contextFactory = contextFactory;
        _sales = sales;
        _permissions = permissions;
        _session = session;
        _installation = installation;
        _audit = audit;
    }

    public async Task<ReceiptData> BuildCurrentAsync(
        SaleResult sale,
        string cashier,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sale);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return ReceiptDataFactory.FromSaleResult(
            sale,
            await ResolveBusinessAsync(context, cancellationToken),
            cashier);
    }

    public async Task<ReceiptData?> GetHistoricalAsync(
        int saleId,
        CancellationToken cancellationToken = default)
    {
        if (saleId <= 0) throw new ArgumentOutOfRangeException(nameof(saleId));
        var detail = await _sales.GetSaleDetailAsync(saleId, cancellationToken);
        if (detail is null) return null;
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return ReceiptDataFactory.FromSalesHistory(
            detail,
            await ResolveBusinessAsync(context, cancellationToken));
    }

    public async Task RegisterReprintAsync(int saleId, CancellationToken cancellationToken = default)
    {
        if (saleId <= 0) throw new ArgumentOutOfRangeException(nameof(saleId));
        _installation.EnsureOperationalWriteAllowed("Reimprimir ticket de venta");
        await _permissions.EnsurePermissionAsync(PermisoEmpleado.COMPROBANTE_REIMPRIMIR, cancellationToken);
        var user = _session.UsuarioActual
            ?? throw new InvalidOperationException("Debe existir una sesion activa.");
        await _audit.RegistrarAsync(new AuditEntryRequest(
            user.UsuarioId,
            TipoBitacora.COMPROBANTE_VENTA_REIMPRESO,
            "ventas",
            saleId,
            $"Se genero nuevamente el ticket historico de la venta {saleId}."), cancellationToken);
    }

    private static async Task<ReceiptBusinessData> ResolveBusinessAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var profile = await BusinessProfileResolver.ResolveAsync(context, cancellationToken);
        return new ReceiptBusinessData(profile.Name, profile.Address, profile.Phone, profile.Email);
    }
}
