using System.Globalization;
using System.Text;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class CustomOrderFinanceDocumentService : ICustomOrderFinanceDocumentService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICustomOrderFinanceQueryService _query;
    private readonly ICurrentSession _session;
    private readonly IAuditService _audit;
    private readonly IReportPathProvider _reportPaths;

    [ActivatorUtilitiesConstructor]
    public CustomOrderFinanceDocumentService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICustomOrderFinanceQueryService query,
        ICurrentSession session,
        IAuditService audit,
        IReportPathProvider reportPaths)
    {
        _contextFactory = contextFactory;
        _query = query;
        _session = session;
        _audit = audit;
        _reportPaths = reportPaths;
    }

    internal CustomOrderFinanceDocumentService(
        AppDbContext context,
        ICustomOrderFinanceQueryService query,
        ICurrentSession session,
        IAuditService audit)
    {
        _legacyContext = context;
        _query = query;
        _session = session;
        _audit = audit;
        _reportPaths = new ReportPathProvider(context);
    }

    public async Task<string> GeneratePaymentPdfAsync(
        int pagoPedidoPersonalizadoId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        var data = await _query.GetPaymentDocumentAsync(pagoPedidoPersonalizadoId, cancellationToken)
            ?? throw new InvalidOperationException("El pago no existe.");
        return await GenerateAsync(data, TipoBitacora.COMPROBANTE_PAGO_PEDIDO_GENERADO,
            "pagos_pedido_personalizado", pagoPedidoPersonalizadoId, cancellationToken);
    }

    public async Task<string> GenerateRefundPdfAsync(
        int reembolsoPedidoPersonalizadoId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        var data = await _query.GetRefundDocumentAsync(reembolsoPedidoPersonalizadoId, cancellationToken)
            ?? throw new InvalidOperationException("El reembolso no existe.");
        return await GenerateAsync(data, TipoBitacora.COMPROBANTE_REEMBOLSO_PEDIDO_GENERADO,
            "reembolsos_pedido_personalizado", reembolsoPedidoPersonalizadoId, cancellationToken);
    }

    private async Task<string> GenerateAsync(
        CustomOrderPaymentDocumentData data,
        TipoBitacora auditType,
        string entity,
        int entityId,
        CancellationToken cancellationToken)
    {
        var context = AppDbContextLease.Current ?? _legacyContext
            ?? throw new InvalidOperationException("No existe un contexto de datos disponible.");
        var profile = await BusinessProfileResolver.ResolveAsync(context, cancellationToken);
        var orderDirectory = await _reportPaths.GetDirectoryAsync("Pedidos personalizados", cancellationToken);
        var directory = Path.Combine(orderDirectory, "Comprobantes");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{data.Codigo}.pdf");
        var title = data.EsReembolso
            ? "COMPROBANTE DE REEMBOLSO"
            : "COMPROBANTE DE PAGO DE PEDIDO";
        var lines = profile.ToDocumentHeaderLines().ToList();
        lines.AddRange([
            title,
            "DOCUMENTO INTERNO NO FISCAL",
            new string('=', 64),
            $"Codigo: {data.Codigo}",
            $"Pedido: {data.Pedido}",
            $"Cliente / destinatario: {data.Destinatario}",
            $"Fecha: {data.Fecha:yyyy-MM-dd HH:mm}",
            $"Tipo: {data.Tipo}",
            $"Metodo: {data.Metodo}",
            $"Referencia: {data.Referencia ?? "-"}",
            $"Monto: {Money(data.MontoCentavos)}",
            $"Saldo anterior: {Money(data.SaldoAnteriorCentavos)}",
            $"Saldo nuevo: {Money(data.SaldoNuevoCentavos)}",
            $"Usuario: {data.Usuario}",
            $"Observaciones: {data.Observaciones ?? "-"}",
            new string('-', 64),
            "Este comprobante no constituye una factura fiscal oficial."
        ]);
        File.WriteAllBytes(path, BuildPdf(lines));
        var userId = _session.UsuarioActual?.UsuarioId
            ?? throw new InvalidOperationException("Debe existir una sesion activa.");
        await _audit.RegistrarAsync(new AuditEntryRequest(
            userId, auditType, entity, entityId,
            $"{title} {data.Codigo} generado en {path}."), cancellationToken);
        return path;
    }

    private static byte[] BuildPdf(IReadOnlyList<string> lines)
    {
        var safeLines = lines.Select(Safe).ToList();
        var streamText = new StringBuilder("BT\n/F1 10 Tf\n14 TL\n50 740 Td\n");
        foreach (var line in safeLines)
            streamText.Append('(').Append(Escape(line)).Append(") Tj\nT*\n");
        var content = streamText.Append("ET").ToString();
        var objects = new[]
        {
            string.Empty,
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>"
        };
        using var output = new MemoryStream();
        Write(output, "%PDF-1.4\n");
        var offsets = new long[objects.Length];
        for (var i = 1; i < objects.Length; i++)
        {
            offsets[i] = output.Position;
            Write(output, $"{i} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = output.Position;
        Write(output, $"xref\n0 {objects.Length}\n0000000000 65535 f \n");
        for (var i = 1; i < objects.Length; i++) Write(output, $"{offsets[i]:0000000000} 00000 n \n");
        Write(output, $"trailer\n<< /Size {objects.Length} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }

    private static string Safe(string value)
    {
        var result = new StringBuilder();
        foreach (var c in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            result.Append(c is >= ' ' and <= '~' ? c : '?');
        }
        return result.ToString();
    }

    private static void Write(Stream stream, string value) => stream.Write(Encoding.ASCII.GetBytes(value));
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    private static string Money(long cents) => (cents / 100m).ToString("$0.00");
}
