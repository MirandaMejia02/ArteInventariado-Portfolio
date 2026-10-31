namespace ArteInventariado.WinForms.Helpers;

/// <summary>
/// Provides toggle-based full screen support for WinForms primary windows.
///
/// Full screen is deliberately a normal Z-order window: making the primary
/// form <see cref="Form.TopMost"/> would allow it to cover secondary forms
/// and native dialogs that do not have an explicit owner. The original
/// TopMost value is still restored when leaving full screen.
/// </summary>
public static class FullScreenHelper
{
    private sealed record FormSnapshot(
        FormBorderStyle BorderStyle,
        FormWindowState WindowState,
        Rectangle Bounds,
        bool TopMost,
        IReadOnlyList<TopMostSnapshot> OwnerTopMostStates);

    private sealed record TopMostSnapshot(Form Form, bool TopMost);

    private static readonly Dictionary<Form, FormSnapshot> _snapshots = [];

    /// <summary>
    /// Toggles full-screen mode for the given form.
    /// Press F11 or call this method to enter/exit full screen.
    /// </summary>
    public static void ToggleFullScreen(Form form)
    {
        if (_snapshots.ContainsKey(form))
        {
            ExitFullScreen(form);
        }
        else
        {
            EnterFullScreen(form);
        }
    }

    /// <summary>
    /// Exits full-screen mode if active. Safe to call even when not in full screen.
    /// </summary>
    public static void ExitFullScreen(Form form)
    {
        if (!_snapshots.TryGetValue(form, out var snapshot))
        {
            return;
        }

        _snapshots.Remove(form);
        form.FormClosed -= RemoveClosedFormSnapshot;

        if (form.IsDisposed)
        {
            return;
        }

        // Keep the form out of the topmost band while its presentation is
        // restored, then put back its original setting exactly as it was.
        form.TopMost = false;
        form.FormBorderStyle = snapshot.BorderStyle;
        form.WindowState = snapshot.WindowState;

        // Only restore bounds if we were in a normal (non-maximized) state
        if (snapshot.WindowState == FormWindowState.Normal)
        {
            form.Bounds = snapshot.Bounds;
        }

        form.TopMost = snapshot.TopMost;
        RestoreOwnerTopMostStates(snapshot);
        FullScreenPresentationCoordinator.OnFullScreenStateChanged();
    }

    public static bool IsFullScreen(Form form) => _snapshots.ContainsKey(form);

    internal static bool HasActiveFullScreenForms => _snapshots.Count > 0;

    internal static Form? FindFullScreenOwner(Form form)
    {
        for (Form? current = form; current is not null; current = current.Owner)
        {
            if (_snapshots.ContainsKey(current))
            {
                return current;
            }
        }

        return null;
    }

    private static void EnterFullScreen(Form form)
    {
        var ownerTopMostStates = GetOwnerChain(form)
            .Where(owner => !owner.IsDisposed)
            .Select(owner => new TopMostSnapshot(owner, owner.TopMost))
            .ToList();
        var snapshot = new FormSnapshot(
            form.FormBorderStyle,
            form.WindowState,
            form.Bounds,
            form.TopMost,
            ownerTopMostStates);

        _snapshots[form] = snapshot;
        form.FormClosed += RemoveClosedFormSnapshot;

        // Must set Normal before removing border to avoid sizing issues
        // F11 must never pin the main window above secondary dialogs.
        foreach (var ownerState in ownerTopMostStates)
        {
            ownerState.Form.TopMost = false;
        }
        form.TopMost = false;
        form.WindowState = FormWindowState.Normal;
        form.FormBorderStyle = FormBorderStyle.None;
        form.WindowState = FormWindowState.Maximized;
        FullScreenPresentationCoordinator.OnFullScreenStateChanged();
    }

    private static void RemoveClosedFormSnapshot(object? sender, FormClosedEventArgs e)
    {
        if (sender is not Form form)
        {
            return;
        }

        if (_snapshots.Remove(form, out var snapshot))
        {
            RestoreOwnerTopMostStates(snapshot);
        }
        form.FormClosed -= RemoveClosedFormSnapshot;
        FullScreenPresentationCoordinator.OnFullScreenStateChanged();
    }

    private static IEnumerable<Form> GetOwnerChain(Form form)
    {
        for (var owner = form.Owner; owner is not null; owner = owner.Owner)
        {
            yield return owner;
        }
    }

    private static void RestoreOwnerTopMostStates(FormSnapshot snapshot)
    {
        foreach (var ownerState in snapshot.OwnerTopMostStates)
        {
            if (!ownerState.Form.IsDisposed)
            {
                ownerState.Form.TopMost = ownerState.TopMost;
            }
        }
    }
}
