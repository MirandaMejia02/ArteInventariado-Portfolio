namespace ArteInventariado.Application.Models;

public sealed record AdvancedReportPrintPage(
    int Number,
    int StartRowIndex,
    IReadOnlyList<int> RowHeights)
{
    public int RowCount => RowHeights.Count;
}

public sealed record AdvancedReportPrintPlan(
    IReadOnlyList<AdvancedReportPrintPage> Pages)
{
    public int TotalPages => Pages.Count;
    public int TotalRows => Pages.Sum(page => page.RowCount);
}

public sealed record AdvancedReportLoadProgress(int LoadedRows, int TotalRows);

public static class AdvancedReportPrintPagination
{
    public static AdvancedReportPrintPlan Create(
        int rowCount,
        int availableHeight,
        Func<int, int> measureRowHeight,
        CancellationToken cancellationToken = default,
        IProgress<int>? pageProgress = null)
    {
        if (rowCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rowCount));
        }
        if (availableHeight < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(availableHeight));
        }
        ArgumentNullException.ThrowIfNull(measureRowHeight);

        if (rowCount == 0)
        {
            return new AdvancedReportPrintPlan([new AdvancedReportPrintPage(1, 0, [])]);
        }

        var pages = new List<AdvancedReportPrintPage>();
        var rowIndex = 0;
        while (rowIndex < rowCount)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = rowIndex;
            var usedHeight = 0;
            var heights = new List<int>();
            while (rowIndex < rowCount)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rowHeight = measureRowHeight(rowIndex);
                if (rowHeight < 1)
                {
                    throw new InvalidOperationException("La altura calculada de una fila debe ser mayor que cero.");
                }
                if (heights.Count > 0 && usedHeight + rowHeight > availableHeight)
                {
                    break;
                }
                if (rowHeight > availableHeight)
                {
                    throw new InvalidOperationException("Una fila excede el área imprimible completa.");
                }

                heights.Add(rowHeight);
                usedHeight += rowHeight;
                rowIndex++;
            }

            pages.Add(new AdvancedReportPrintPage(pages.Count + 1, start, heights));
            pageProgress?.Report(pages.Count);
        }

        return new AdvancedReportPrintPlan(pages);
    }
}

public sealed class AdvancedReportPrintCursor
{
    private readonly AdvancedReportPrintPlan _plan;
    private int _nextPageIndex;

    public AdvancedReportPrintCursor(AdvancedReportPrintPlan plan)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
    }

    public void Begin() => _nextPageIndex = 0;

    public bool TryTakeNext(out AdvancedReportPrintPage? page)
    {
        if (_nextPageIndex >= _plan.Pages.Count)
        {
            page = null;
            return false;
        }

        page = _plan.Pages[_nextPageIndex++];
        return true;
    }

    public bool HasMorePages => _nextPageIndex < _plan.Pages.Count;
}
