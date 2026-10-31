using System.Globalization;
using System.Text;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class QuotationDocumentService : IQuotationDocumentService
{
    private const int PageWidth = 612;
    private const int PageHeight = 792;
    private const int LeftMargin = 45;
    private const int Top = 750;
    private const int LinesPerPage = 57;
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly IQuotationQueryService _query;
    private readonly ICurrentSession _session;
    private readonly IAuditService _audit;
    private readonly IEmployeePermissionService _permissions;
    private readonly IReportPathProvider _reportPaths;

    [ActivatorUtilitiesConstructor]
    public QuotationDocumentService(
        IDbContextFactory<AppDbContext> contextFactory,
        IQuotationQueryService query,
        ICurrentSession session,
        IAuditService audit,
        IEmployeePermissionService permissions,
        IReportPathProvider reportPaths)
    {
        _contextFactory = contextFactory;
        _query = query;
        _session = session;
        _audit = audit;
        _permissions = permissions;
        _reportPaths = reportPaths;
    }

    internal QuotationDocumentService(
        AppDbContext context,
        IQuotationQueryService query,
        ICurrentSession session,
        IAuditService audit,
        IEmployeePermissionService permissions)
    {
        _legacyContext = context;
        _query = query;
        _session = session;
        _audit = audit;
        _permissions = permissions;
        _reportPaths = new ReportPathProvider(context);
    }

    public async Task<string> GeneratePdfAsync(
        int cotizacionId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        await _permissions.EnsurePermissionAsync(PermisoEmpleado.COTIZACIONES_ACCEDER, cancellationToken);
        var quotation = await _query.GetByIdAsync(cotizacionId, cancellationToken)
            ?? throw new InvalidOperationException("La cotizacion no existe.");
        var profile = await BusinessProfileResolver.ResolveAsync(lease.Context, cancellationToken);
        var directory = await _reportPaths.GetDirectoryAsync("Cotizaciones", cancellationToken);
        var filePath = Path.Combine(directory, $"{quotation.CodigoCotizacion}.pdf");
        File.WriteAllBytes(filePath, BuildPdf(BuildLines(profile, quotation)));

        var userId = _session.UsuarioActual?.UsuarioId
            ?? throw new InvalidOperationException("Debe existir una sesion activa.");
        await _audit.RegistrarAsync(new AuditEntryRequest(
            userId,
            TipoBitacora.COTIZACION_PDF_GENERADO,
            "cotizaciones",
            cotizacionId,
            $"PDF de cotizacion {quotation.CodigoCotizacion} generado en {filePath}."), cancellationToken);
        return filePath;
    }

    private static IReadOnlyList<string> BuildLines(BusinessProfile profile, QuotationDetail q)
    {
        var lines = profile.ToDocumentHeaderLines().ToList();
        lines.AddRange([
            "COTIZACION - DOCUMENTO NO FISCAL",
            new string('=', 72),
            $"Codigo: {q.CodigoCotizacion}",
            $"Estado: {q.Estado}",
            $"Fecha: {q.Fecha:yyyy-MM-dd}    Vigencia hasta: {q.FechaVencimiento:yyyy-MM-dd}",
            $"Destinatario: {q.Destinatario}",
            $"Telefono: {q.Telefono ?? "-"}",
            $"Correo: {q.Correo ?? "-"}",
            $"Direccion: {q.Direccion ?? "-"}",
            new string('-', 72),
            "CODIGO       DESCRIPCION                    CANT.   P.UNIT.   DESC.   TOTAL"
        ]);
        foreach (var line in q.Detalles)
        {
            lines.Add(
                $"{Fit(line.Codigo ?? "-", 12)} " +
                $"{Fit(line.Descripcion, 30)} " +
                $"{line.Cantidad,5} " +
                $"{Money(line.PrecioUnitarioCentavos),9} " +
                $"{Money(line.DescuentoLineaCentavos),7} " +
                $"{Money(line.SubtotalCentavos),9}");
            if (!string.IsNullOrWhiteSpace(line.Observaciones))
            {
                lines.Add($"  Nota: {line.Observaciones}");
            }
        }
        lines.AddRange([
            new string('-', 72),
            $"Subtotal: {Money(q.SubtotalCentavos),12}",
            $"Descuento: {Money(q.DescuentoGeneralCentavos),11}",
            $"TOTAL: {Money(q.TotalCentavos),15}",
            string.Empty,
            $"Condiciones: {q.CondicionesComerciales ?? "-"}",
            $"Observaciones: {q.Observaciones ?? "-"}",
            string.Empty,
            "Este documento es una cotizacion comercial y no es una factura fiscal."
        ]);
        return lines;
    }

    private static byte[] BuildPdf(IReadOnlyList<string> rawLines)
    {
        var pages = rawLines.Select(ToPdfSafeText).Chunk(LinesPerPage).Select(x => x.ToList()).ToList();
        if (pages.Count == 0) pages.Add([]);
        var count = 2 + pages.Count * 2 + 1;
        var font = count;
        var objects = new string[count + 1];
        objects[1] = "<< /Type /Catalog /Pages 2 0 R >>";
        var kids = new List<string>();
        for (var i = 0; i < pages.Count; i++)
        {
            var page = 3 + i * 2;
            var content = page + 1;
            kids.Add($"{page} 0 R");
            objects[page] = $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth} {PageHeight}] /Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>";
            var stream = BuildPageContent(pages[i]);
            objects[content] = $"<< /Length {Encoding.ASCII.GetByteCount(stream)} >>\nstream\n{stream}\nendstream";
        }
        objects[2] = $"<< /Type /Pages /Kids [{string.Join(" ", kids)}] /Count {pages.Count} >>";
        objects[font] = "<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>";
        using var output = new MemoryStream();
        WriteAscii(output, "%PDF-1.4\n");
        var offsets = new long[count + 1];
        for (var i = 1; i <= count; i++)
        {
            offsets[i] = output.Position;
            WriteAscii(output, $"{i} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = output.Position;
        WriteAscii(output, $"xref\n0 {count + 1}\n0000000000 65535 f \n");
        for (var i = 1; i <= count; i++) WriteAscii(output, $"{offsets[i]:0000000000} 00000 n \n");
        WriteAscii(output, $"trailer\n<< /Size {count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }

    private static string BuildPageContent(IReadOnlyList<string> lines)
    {
        var text = new StringBuilder($"BT\n/F1 9 Tf\n12 TL\n{LeftMargin} {Top} Td\n");
        foreach (var line in lines)
        {
            text.Append('(').Append(Escape(line)).Append(") Tj\nT*\n");
        }
        return text.Append("ET").ToString();
    }

    private static string Money(long cents) => (cents / 100m).ToString("$0.00");
    private static string Fit(string value, int length) =>
        value.Length <= length ? value.PadRight(length) : value[..(length - 3)] + "...";
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    private static string ToPdfSafeText(string value)
    {
        var builder = new StringBuilder();
        foreach (var c in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(c is >= ' ' and <= '~' ? c : '?');
        }
        return builder.ToString();
    }
    private static void WriteAscii(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes);
    }
}
