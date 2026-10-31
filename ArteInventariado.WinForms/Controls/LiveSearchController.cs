using ArteInventariado.Application.Services;

namespace ArteInventariado.WinForms.Controls;

/// <summary>
/// Reusable WinForms binding for debounced text search, immediate Enter and clear affordance.
/// </summary>
internal sealed class LiveSearchController : IDisposable
{
    private readonly TextBox _input;
    private readonly Button _clearButton;
    private readonly Func<CancellationToken, Task> _searchAsync;
    private readonly Action<Exception> _showError;
    private readonly Action? _onClearAdditionalState;
    private readonly Func<bool>? _hasAdditionalClearableState;
    private readonly LiveSearchCoordinator _coordinator;
    private readonly bool _handleEnter;
    private bool _disposed;

    public LiveSearchController(
        TextBox input,
        Button clearButton,
        Func<CancellationToken, Task> searchAsync,
        Action<Exception> showError,
        TimeSpan? delay = null,
        bool handleEnter = true,
        Action? onClearAdditionalState = null,
        Func<bool>? hasAdditionalClearableState = null)
    {
        _input = input;
        _clearButton = clearButton;
        _searchAsync = searchAsync;
        _showError = showError;
        _onClearAdditionalState = onClearAdditionalState;
        _hasAdditionalClearableState = hasAdditionalClearableState;
        _coordinator = new LiveSearchCoordinator(delay);
        _handleEnter = handleEnter;

        _input.TextChanged += Input_TextChanged;
        if (_handleEnter)
        {
            _input.KeyDown += Input_KeyDown;
        }
        _clearButton.Click += ClearButton_Click;
        UpdateClearButton();
    }

    public void CancelPending() => _coordinator.Cancel();

    public Task SearchNowAsync() => RunSafelyAsync(immediate: true);

    public Task ExecuteNowAsync(Func<CancellationToken, Task> operation) =>
        RunSafelyAsync(immediate: true, operation);

    public bool ShouldShowClearButton => !string.IsNullOrEmpty(_input.Text) ||
        (_hasAdditionalClearableState?.Invoke() ?? false);

    public void RefreshClearButton() => UpdateClearButton();

    public void Clear()
    {
        _input.Clear();
        _onClearAdditionalState?.Invoke();
        _input.Focus();
        UpdateClearButton();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _input.TextChanged -= Input_TextChanged;
        if (_handleEnter)
        {
            _input.KeyDown -= Input_KeyDown;
        }
        _clearButton.Click -= ClearButton_Click;
        _coordinator.Dispose();
    }

    private async void Input_TextChanged(object? sender, EventArgs e)
    {
        UpdateClearButton();
        await RunSafelyAsync(immediate: false);
    }

    private async void Input_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
        {
            return;
        }

        e.Handled = true;
        e.SuppressKeyPress = true;
        await RunSafelyAsync(immediate: true);
    }

    private void ClearButton_Click(object? sender, EventArgs e) => Clear();

    private async Task RunSafelyAsync(
        bool immediate,
        Func<CancellationToken, Task>? operation = null)
    {
        try
        {
            var action = operation ?? _searchAsync;
            if (immediate)
            {
                await _coordinator.ExecuteNowAsync(action);
            }
            else
            {
                await _coordinator.ScheduleAsync(action);
            }
        }
        catch (ObjectDisposedException) when (_disposed)
        {
            // The owning form closed while its delayed search was pending.
        }
        catch (OperationCanceledException)
        {
            // The owning query can also surface cooperative cancellation directly.
        }
        catch (Exception ex)
        {
            _showError(ex);
        }
    }

    private void UpdateClearButton()
    {
        _clearButton.Visible = ShouldShowClearButton;
        _clearButton.Enabled = _clearButton.Visible;
    }
}
