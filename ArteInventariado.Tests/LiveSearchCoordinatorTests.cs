using System.Diagnostics;
using ArteInventariado.Application.Services;

namespace ArteInventariado.Tests;

public class LiveSearchCoordinatorTests
{
    [Fact]
    public async Task CharacterByCharacterSearch_DebouncesAndRunsOnlyLatestText()
    {
        using var coordinator = new LiveSearchCoordinator(TimeSpan.FromMilliseconds(100));
        var executed = new List<string>();

        var first = coordinator.ScheduleAsync(_ => CaptureAsync("a"));
        var second = coordinator.ScheduleAsync(_ => CaptureAsync("al"));
        var third = coordinator.ScheduleAsync(_ => CaptureAsync("alcancia"));

        await Task.WhenAll(first, second, third);

        Assert.Equal(["alcancia"], executed);

        Task CaptureAsync(string text)
        {
            executed.Add(text);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Debounce_WaitsBeforeExecuting()
    {
        var delay = TimeSpan.FromMilliseconds(120);
        using var coordinator = new LiveSearchCoordinator(delay);
        var executions = 0;
        var startedAt = Stopwatch.GetTimestamp();

        await coordinator.ScheduleAsync(_ =>
        {
            executions++;
            return Task.CompletedTask;
        });

        Assert.Equal(1, executions);
        Assert.True(
            Stopwatch.GetElapsedTime(startedAt) >= delay - TimeSpan.FromMilliseconds(10),
            $"La busqueda se ejecuto antes del debounce configurado de {delay.TotalMilliseconds} ms.");
    }

    [Fact]
    public async Task NewSearch_CancelsRunningQueryBeforeStartingReplacement()
    {
        using var coordinator = new LiveSearchCoordinator(TimeSpan.Zero);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacementRan = false;

        var first = coordinator.ExecuteNowAsync(async token =>
        {
            firstStarted.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException)
            {
                firstCancelled.SetResult();
                throw;
            }
        });
        await firstStarted.Task;

        var replacement = coordinator.ExecuteNowAsync(_ =>
        {
            replacementRan = true;
            return Task.CompletedTask;
        });

        await Task.WhenAll(first, replacement, firstCancelled.Task);
        Assert.True(replacementRan);
    }

    [Fact]
    public async Task Replacement_WaitsForCancelledQueryToFinishAndNeverOverlaps()
    {
        using var coordinator = new LiveSearchCoordinator(TimeSpan.Zero);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = false;
        var activeQueries = 0;
        var maximumConcurrentQueries = 0;

        var first = coordinator.ExecuteNowAsync(async _ =>
        {
            var active = Interlocked.Increment(ref activeQueries);
            maximumConcurrentQueries = Math.Max(maximumConcurrentQueries, active);
            firstStarted.SetResult();
            await releaseFirst.Task;
            Interlocked.Decrement(ref activeQueries);
        });
        await firstStarted.Task;

        var second = coordinator.ExecuteNowAsync(_ =>
        {
            secondStarted = true;
            var active = Interlocked.Increment(ref activeQueries);
            maximumConcurrentQueries = Math.Max(maximumConcurrentQueries, active);
            Interlocked.Decrement(ref activeQueries);
            return Task.CompletedTask;
        });

        await Task.Delay(20);
        Assert.False(secondStarted);
        releaseFirst.SetResult();
        await Task.WhenAll(first, second);

        Assert.True(secondStarted);
        Assert.Equal(1, maximumConcurrentQueries);
    }

    [Fact]
    public async Task EmptyText_CanRestoreNormalListingAndResetPagination()
    {
        using var coordinator = new LiveSearchCoordinator(TimeSpan.Zero);
        var page = 4;
        var search = "previous";
        var normalListingLoaded = false;

        search = string.Empty;
        await coordinator.ExecuteNowAsync(_ =>
        {
            page = 1;
            normalListingLoaded = string.IsNullOrWhiteSpace(search);
            return Task.CompletedTask;
        });

        Assert.Equal(1, page);
        Assert.True(normalListingLoaded);
    }
}
