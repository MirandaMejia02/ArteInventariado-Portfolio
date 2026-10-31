namespace ArteInventariado.Application.Services;

/// <summary>
/// Coordinates debounced searches so only the most recent request remains active.
/// Search operations must honor the supplied token and check it before publishing results.
/// </summary>
public sealed class LiveSearchCoordinator : IDisposable
{
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(300);

    private readonly object _sync = new();
    private readonly TimeSpan _delay;
    private CancellationTokenSource? _activeSearch;
    private Task _activeExecution = Task.CompletedTask;
    private long _version;
    private bool _disposed;

    public LiveSearchCoordinator(TimeSpan? delay = null)
    {
        _delay = delay ?? DefaultDelay;
        if (_delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay));
        }
    }

    public Task ScheduleAsync(Func<CancellationToken, Task> searchAsync) =>
        StartAsync(searchAsync, debounce: true);

    public Task ExecuteNowAsync(Func<CancellationToken, Task> searchAsync) =>
        StartAsync(searchAsync, debounce: false);

    public void Cancel()
    {
        CancellationTokenSource? active;
        lock (_sync)
        {
            active = _activeSearch;
            _activeSearch = null;
            _version++;
        }

        TryCancel(active);
    }

    public void Dispose()
    {
        CancellationTokenSource? active;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            active = _activeSearch;
            _activeSearch = null;
            _version++;
        }

        TryCancel(active);
        active?.Dispose();
    }

    private Task StartAsync(Func<CancellationToken, Task> searchAsync, bool debounce)
    {
        ArgumentNullException.ThrowIfNull(searchAsync);

        CancellationTokenSource? previous;
        CancellationTokenSource current;
        Task previousExecution;
        var currentCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        long version;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            previous = _activeSearch;
            previousExecution = _activeExecution;
            current = new CancellationTokenSource();
            _activeSearch = current;
            version = ++_version;
            _activeExecution = currentCompletion.Task;
        }

        TryCancel(previous);
        _ = RunAsync(searchAsync, debounce, current, version, previousExecution, currentCompletion);
        return currentCompletion.Task;
    }

    private async Task RunAsync(
        Func<CancellationToken, Task> searchAsync,
        bool debounce,
        CancellationTokenSource source,
        long version,
        Task previousExecution,
        TaskCompletionSource completion)
    {
        var completionPublished = false;
        await Task.Yield();
        try
        {
            var debounceTask = debounce && _delay > TimeSpan.Zero
                ? Task.Delay(_delay, source.Token)
                : Task.CompletedTask;

            try
            {
                await previousExecution;
            }
            catch
            {
                // A failed older request must not prevent the latest search from running.
            }

            if (debounce && _delay > TimeSpan.Zero)
            {
                await debounceTask;
            }

            EnsureCurrent(source, version);
            await searchAsync(source.Token);
            source.Token.ThrowIfCancellationRequested();

            EnsureCurrent(source, version);
            completion.TrySetResult();
            completionPublished = true;
        }
        catch (OperationCanceledException) when (IsExpectedCancellation(source, version))
        {
            // A newer search superseded this one. Cancellation is the expected outcome.
            completion.TrySetResult();
            completionPublished = true;
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
            completionPublished = true;
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_activeSearch, source))
                {
                    _activeSearch = null;
                    if (ReferenceEquals(_activeExecution, completion.Task))
                    {
                        _activeExecution = Task.CompletedTask;
                    }
                }
            }

            if (!completionPublished)
            {
                completion.TrySetCanceled();
            }

            source.Dispose();
        }
    }

    private void EnsureCurrent(CancellationTokenSource source, long version)
    {
        lock (_sync)
        {
            if (_disposed || _version != version || !ReferenceEquals(_activeSearch, source))
            {
                throw new OperationCanceledException();
            }
        }
    }

    private bool IsExpectedCancellation(CancellationTokenSource source, long version)
    {
        try
        {
            if (source.IsCancellationRequested)
            {
                return true;
            }
        }
        catch (ObjectDisposedException)
        {
            return true;
        }

        lock (_sync)
        {
            return _disposed || _version != version || !ReferenceEquals(_activeSearch, source);
        }
    }

    private static void TryCancel(CancellationTokenSource? source)
    {
        try
        {
            source?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A completing search can dispose its source concurrently with replacement.
        }
    }
}
