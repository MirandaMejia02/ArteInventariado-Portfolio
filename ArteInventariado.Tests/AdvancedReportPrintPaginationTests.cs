using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;

namespace ArteInventariado.Tests;

public class AdvancedReportPrintPaginationTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 1)]
    [InlineData(6, 2)]
    [InlineData(25, 5)]
    [InlineData(105, 21)]
    public void Create_ProducesExactPageCountWithoutDuplicatingRows(int rowCount, int expectedPages)
    {
        var plan = AdvancedReportPrintPagination.Create(rowCount, 100, _ => 20);

        Assert.Equal(expectedPages, plan.TotalPages);
        Assert.Equal(rowCount, plan.TotalRows);
        Assert.Equal(Enumerable.Range(0, rowCount), RowIndexes(plan));
    }

    [Fact]
    public void Create_MultilineRowsMoveWholeToNextPage()
    {
        int[] heights = [30, 30, 55, 30];

        var plan = AdvancedReportPrintPagination.Create(heights.Length, 100, index => heights[index]);

        Assert.Equal(2, plan.TotalPages);
        Assert.Equal([30, 30], plan.Pages[0].RowHeights);
        Assert.Equal([55, 30], plan.Pages[1].RowHeights);
        Assert.Equal(2, plan.Pages[1].StartRowIndex);
    }

    [Fact]
    public void Cursor_BeginResetsPreviewAndPhysicalPrintToPageOne()
    {
        var plan = AdvancedReportPrintPagination.Create(11, 100, _ => 20);
        var cursor = new AdvancedReportPrintCursor(plan);

        Assert.True(cursor.TryTakeNext(out var previewFirst));
        Assert.True(cursor.TryTakeNext(out _));
        cursor.Begin();
        Assert.True(cursor.TryTakeNext(out var reopenedFirst));
        cursor.Begin();
        Assert.True(cursor.TryTakeNext(out var printedFirst));

        Assert.Equal(1, previewFirst!.Number);
        Assert.Equal(1, reopenedFirst!.Number);
        Assert.Equal(1, printedFirst!.Number);
    }

    [Fact]
    public void Create_HonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            AdvancedReportPrintPagination.Create(100, 100, _ => 20, cancellation.Token));
    }

    [Fact]
    public async Task LoadAllAsync_UsesSameRowsForPreparationPreviewAndPrint()
    {
        var query = new PagedQueryService(totalRows: 2_105);
        var progress = new List<AdvancedReportLoadProgress>();

        var report = await AdvancedReportDataLoader.LoadAllAsync(
            query,
            Request(),
            new InlineProgress<AdvancedReportLoadProgress>(progress.Add));

        Assert.Equal(2_105, report.Rows.Count);
        Assert.Equal([1, 2, 3], query.RequestedPages);
        Assert.Equal(2_105, progress[^1].LoadedRows);
        Assert.Equal(Enumerable.Range(1, 2_105), report.Rows.Select(row => (int)row["number"]!));
    }

    [Fact]
    public async Task LoadAllAsync_CancellationNeverReturnsPartialPreview()
    {
        using var cancellation = new CancellationTokenSource();
        var query = new PagedQueryService(2_105, pageLoaded: page =>
        {
            if (page == 1)
            {
                cancellation.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AdvancedReportDataLoader.LoadAllAsync(query, Request(), cancellationToken: cancellation.Token));
    }

    private static IEnumerable<int> RowIndexes(AdvancedReportPrintPlan plan) =>
        plan.Pages.SelectMany(page => Enumerable.Range(page.StartRowIndex, page.RowCount));

    private static AdvancedReportRequest Request() => new(
        AdvancedReportKind.AdministrativeActivity,
        new AnalyticsQueryRequest(new AnalyticsPeriodRequest(AnalyticsPeriodKind.LastThreeMonths)),
        Search: "actividad",
        UserId: 7);

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class PagedQueryService(int totalRows, Action<int>? pageLoaded = null) : IAdvancedReportQueryService
    {
        public List<int> RequestedPages { get; } = [];

        public Task<AdvancedReportResult> QueryAsync(
            AdvancedReportRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedPages.Add(request.Page);
            var skip = (request.Page - 1) * request.PageSize;
            var count = Math.Max(0, Math.Min(request.PageSize, totalRows - skip));
            var rows = Enumerable.Range(skip + 1, count)
                .Select(number => new AdvancedReportRow(new Dictionary<string, object?>
                {
                    ["number"] = number,
                    ["description"] = $"Actividad administrativa {number}"
                }))
                .ToList();
            pageLoaded?.Invoke(request.Page);
            return Task.FromResult(new AdvancedReportResult(
                AdvancedReportCatalog.Get(request.Kind),
                new AnalyticsPeriodDefinition(
                    AnalyticsPeriodKind.LastThreeMonths,
                    DateTimeOffset.UtcNow.AddMonths(-3),
                    DateTimeOffset.UtcNow,
                    DateTime.UtcNow.AddMonths(-3),
                    DateTime.UtcNow,
                    AnalyticsGrouping.Day,
                    "Últimos 3 meses",
                    false),
                ["Búsqueda: actividad", "Usuario: admin"],
                [new("number", "Número", AdvancedReportValueKind.Integer), new("description", "Descripción")],
                rows,
                totalRows,
                [],
                null,
                "Sin datos"));
        }
    }
}
