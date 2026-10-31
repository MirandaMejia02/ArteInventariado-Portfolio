using System.Globalization;
using System.Text;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.Infrastructure.Services;

public class CustomOrderDocumentService : ICustomOrderDocumentService
{
    private readonly IDbContextFactory<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _legacyContext;
    private readonly ICustomOrderQueryService _query;
    private readonly ICurrentSession _session;
    private readonly IAuditService _audit;
    private readonly IEmployeePermissionService _permissions;
    private readonly IReportPathProvider _reportPaths;

    [ActivatorUtilitiesConstructor]
    public CustomOrderDocumentService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICustomOrderQueryService query,
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

    internal CustomOrderDocumentService(
        AppDbContext context,
        ICustomOrderQueryService query,
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
        int pedidoPersonalizadoId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AppDbContextLease.CreateAsync(
            _contextFactory, _legacyContext, cancellationToken);
        await _permissions.EnsurePermissionAsync(PermisoEmpleado.PEDIDOS_ACCEDER, cancellationToken);
        var order = await _query.GetByIdAsync(pedidoPersonalizadoId, cancellationToken)
            ?? throw new InvalidOperationException("El pedido no existe.");
        var profile = await BusinessProfileResolver.ResolveAsync(lease.Context, cancellationToken);
        var directory = await _reportPaths.GetDirectoryAsync("Pedidos personalizados", cancellationToken);
        var path = Path.Combine(directory, $"{order.CodigoPedido}.pdf");
        File.WriteAllBytes(path, SimplePdf.Build(BuildLines(profile, order)));
        var userId = _session.UsuarioActual?.UsuarioId
            ?? throw new InvalidOperationException("Debe existir una sesion activa.");
        await _audit.RegistrarAsync(new AuditEntryRequest(
            userId, TipoBitacora.PEDIDO_PDF_GENERADO, "pedidos_personalizados",
            pedidoPersonalizadoId, $"PDF del pedido {order.CodigoPedido} generado en {path}."), cancellationToken);
        return path;
    }

    private static IReadOnlyList<string> BuildLines(BusinessProfile profile, CustomOrderDetail order)
    {
        var lines = profile.ToDocumentHeaderLines().ToList();
        lines.AddRange([
            "PEDIDO PERSONALIZADO - DOCUMENTO NO FISCAL",
            new string('=', 72),
            $"Pedido: {order.CodigoPedido}    Cotizacion: {order.CodigoCotizacion ?? "-"}",
            $"Estado: {order.Estado}    Prioridad: {order.Prioridad}",
            $"Fecha: {order.FechaPedido:yyyy-MM-dd}    Entrega prometida: {order.FechaPrometida:yyyy-MM-dd}",
            $"Destinatario: {order.Destinatario}",
            $"Telefono: {order.Telefono ?? "-"}    Correo: {order.Correo ?? "-"}",
            $"Direccion: {order.Direccion ?? "-"}",
            new string('-', 72),
            "CODIGO       DESCRIPCION                    CANT.   P.UNIT.   DESC.   TOTAL"
        ]);
        foreach (var item in order.Detalles)
        {
            lines.Add($"{Fit(item.Codigo ?? "-", 12)} {Fit(item.Descripcion, 30)} {item.Cantidad,5} " +
                $"{Money(item.PrecioUnitarioCentavos),9} {Money(item.DescuentoLineaCentavos),7} {Money(item.SubtotalCentavos),9}");
            AddIfPresent(lines, "  Especificaciones: ", item.Especificaciones);
            AddIfPresent(lines, "  Color: ", item.Color);
            AddIfPresent(lines, "  Medidas: ", item.Medidas);
            AddIfPresent(lines, "  Material: ", item.Material);
            AddIfPresent(lines, "  Texto: ", item.TextoPersonalizado);
            AddIfPresent(lines, "  Nota: ", item.Observaciones);
        }
        lines.AddRange([
            new string('-', 72),
            $"Subtotal: {Money(order.SubtotalCentavos),12}",
            $"Descuento: {Money(order.DescuentoGeneralCentavos),11}",
            $"TOTAL ESTIMADO: {Money(order.TotalEstimadoCentavos),6}",
            string.Empty,
            $"Descripcion: {order.DescripcionGeneral ?? "-"}",
            $"Especificaciones generales: {order.EspecificacionesGenerales ?? "-"}",
            $"Notas para el cliente: {order.NotasCliente ?? "-"}",
            string.Empty,
            "Este documento es un pedido interno y no es una factura fiscal."
        ]);
        return lines;
    }

    private static void AddIfPresent(List<string> lines, string prefix, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) lines.Add(prefix + value);
    }
    private static string Money(long cents) => (cents / 100m).ToString("$0.00");
    private static string Fit(string value, int length) =>
        value.Length <= length ? value.PadRight(length) : value[..(length - 3)] + "...";

    private static class SimplePdf
    {
        private const int LinesPerPage = 57;

        public static byte[] Build(IReadOnlyList<string> lines)
        {
            var pages = lines.Select(Safe).Chunk(LinesPerPage).Select(c => c.ToList()).ToList();
            if (pages.Count == 0) pages.Add([]);
            var objectCount = 3 + pages.Count * 2;
            var fontId = objectCount;
            var objects = new string[objectCount + 1];
            objects[1] = "<< /Type /Catalog /Pages 2 0 R >>";
            var kids = new List<string>();
            for (var index = 0; index < pages.Count; index++)
            {
                var pageId = 3 + index * 2;
                var contentId = pageId + 1;
                kids.Add($"{pageId} 0 R");
                objects[pageId] = $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {fontId} 0 R >> >> /Contents {contentId} 0 R >>";
                var stream = PageContent(pages[index]);
                objects[contentId] = $"<< /Length {Encoding.ASCII.GetByteCount(stream)} >>\nstream\n{stream}\nendstream";
            }
            objects[2] = $"<< /Type /Pages /Kids [{string.Join(" ", kids)}] /Count {pages.Count} >>";
            objects[fontId] = "<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>";
            using var output = new MemoryStream();
            Write(output, "%PDF-1.4\n");
            var offsets = new long[objectCount + 1];
            for (var i = 1; i <= objectCount; i++)
            {
                offsets[i] = output.Position;
                Write(output, $"{i} 0 obj\n{objects[i]}\nendobj\n");
            }
            var xref = output.Position;
            Write(output, $"xref\n0 {objectCount + 1}\n0000000000 65535 f \n");
            for (var i = 1; i <= objectCount; i++) Write(output, $"{offsets[i]:0000000000} 00000 n \n");
            Write(output, $"trailer\n<< /Size {objectCount + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            return output.ToArray();
        }

        private static string PageContent(IEnumerable<string> lines)
        {
            var text = new StringBuilder("BT\n/F1 9 Tf\n12 TL\n45 750 Td\n");
            foreach (var line in lines) text.Append('(').Append(Escape(line)).Append(") Tj\nT*\n");
            return text.Append("ET").ToString();
        }
        private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
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
    }
}
