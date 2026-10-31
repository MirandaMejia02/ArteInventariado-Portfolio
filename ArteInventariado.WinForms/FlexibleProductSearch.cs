using System.Globalization;
using System.Text;
using ArteInventariado.Application.Models;

namespace ArteInventariado.WinForms;

public static class FlexibleProductSearch
{
    public static IReadOnlyList<ProductSummary> Filter(
        IEnumerable<ProductSummary> products,
        string? searchText,
        int? maxResults = null,
        CancellationToken cancellationToken = default)
    {
        var queryTokens = Tokenize(searchText);
        if (queryTokens.Count == 0)
        {
            return maxResults.HasValue
                ? products.Take(maxResults.Value).ToList()
                : products.ToList();
        }

        var results = new List<ProductSummary>();

        foreach (var product in products)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsMatch(product, queryTokens))
            {
                continue;
            }

            results.Add(product);

            if (maxResults.HasValue && results.Count >= maxResults.Value)
            {
                break;
            }
        }

        return results;
    }

    private static bool IsMatch(ProductSummary product, IReadOnlyList<string> queryTokens)
    {
        var targetTokens = Tokenize($"{product.CodigoInterno} {product.Nombre} {product.CategoriaNombre}");
        return queryTokens.All(query => targetTokens.Any(target => TokenMatches(query, target)));
    }

    private static bool TokenMatches(string query, string target)
    {
        if (target.Contains(query, StringComparison.Ordinal) ||
            query.Contains(target, StringComparison.Ordinal))
        {
            return true;
        }

        if (query.Length < 4 || target.Length < 4)
        {
            return false;
        }

        return IsWithinDistance(query, target, 1);
    }

    private static List<string> Tokenize(string? value)
    {
        var normalized = Normalize(value);
        return normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .ToList();
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(character) ? character : ' ');
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static bool IsWithinDistance(string source, string target, int maxDistance)
    {
        if (Math.Abs(source.Length - target.Length) > maxDistance)
        {
            return false;
        }

        var previous = new int[target.Length + 1];
        var current = new int[target.Length + 1];

        for (var j = 0; j <= target.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= source.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= target.Length; j++)
            {
                var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            if (current.Min() > maxDistance)
            {
                return false;
            }

            (previous, current) = (current, previous);
        }

        return previous[target.Length] <= maxDistance;
    }
}
