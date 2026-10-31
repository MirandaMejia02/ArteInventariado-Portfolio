using System.Globalization;
using System.Text;

namespace ArteInventariado.WinForms;

public static class AccountPaymentReceiptPdfExporter
{
    private const int PageWidth = 612;
    private const int PageHeight = 792;
    private const int LeftMargin = 54;
    private const int Top = 750;
    private const int LinesPerPage = 58;

    public static void Save(AccountPaymentReceiptViewModel receipt, string filePath)
    {
        SaveLines(receipt.BuildReceiptLines(), filePath);
    }

    public static void SaveLines(IEnumerable<string> receiptLines, string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var lines = receiptLines
            .Select(ToPdfSafeText)
            .ToList();
        var pages = lines
            .Chunk(LinesPerPage)
            .Select(chunk => chunk.ToList())
            .ToList();

        if (pages.Count == 0)
        {
            pages.Add([]);
        }

        File.WriteAllBytes(filePath, BuildPdf(pages));
    }

    private static byte[] BuildPdf(IReadOnlyList<IReadOnlyList<string>> pages)
    {
        var objectCount = 2 + pages.Count * 2 + 1;
        var fontObjectNumber = objectCount;
        var objects = new string[objectCount + 1];

        objects[1] = "<< /Type /Catalog /Pages 2 0 R >>";

        var kids = new List<string>();
        for (var i = 0; i < pages.Count; i++)
        {
            var pageObjectNumber = 3 + i * 2;
            var contentObjectNumber = pageObjectNumber + 1;
            kids.Add($"{pageObjectNumber} 0 R");

            objects[pageObjectNumber] =
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth} {PageHeight}] /Resources << /Font << /F1 {fontObjectNumber} 0 R >> >> /Contents {contentObjectNumber} 0 R >>";

            var content = BuildPageContent(pages[i]);
            var contentLength = Encoding.ASCII.GetByteCount(content);
            objects[contentObjectNumber] = $"<< /Length {contentLength} >>\nstream\n{content}\nendstream";
        }

        objects[2] = $"<< /Type /Pages /Kids [{string.Join(" ", kids)}] /Count {pages.Count} >>";
        objects[fontObjectNumber] = "<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>";

        using var stream = new MemoryStream();
        WriteAscii(stream, "%PDF-1.4\n");

        var offsets = new long[objectCount + 1];
        for (var i = 1; i <= objectCount; i++)
        {
            offsets[i] = stream.Position;
            WriteAscii(stream, $"{i} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xrefOffset = stream.Position;
        WriteAscii(stream, $"xref\n0 {objectCount + 1}\n");
        WriteAscii(stream, "0000000000 65535 f \n");
        for (var i = 1; i <= objectCount; i++)
        {
            WriteAscii(stream, $"{offsets[i]:0000000000} 00000 n \n");
        }

        WriteAscii(stream, $"trailer\n<< /Size {objectCount + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return stream.ToArray();
    }

    private static string BuildPageContent(IReadOnlyList<string> lines)
    {
        var builder = new StringBuilder();
        builder.AppendLine("BT");
        builder.AppendLine("/F1 10 Tf");
        builder.AppendLine("12 TL");
        builder.AppendLine($"{LeftMargin} {Top} Td");

        foreach (var line in lines)
        {
            builder.Append('(');
            builder.Append(EscapePdfString(line));
            builder.AppendLine(") Tj");
            builder.AppendLine("T*");
        }

        builder.Append("ET");
        return builder.ToString();
    }

    private static string EscapePdfString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("(", "\\(")
            .Replace(")", "\\)");
    }

    private static string ToPdfSafeText(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(character is >= ' ' and <= '~' ? character : '?');
        }

        return builder.ToString();
    }

    private static void WriteAscii(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }
}
