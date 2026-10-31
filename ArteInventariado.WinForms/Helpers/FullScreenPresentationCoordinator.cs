namespace ArteInventariado.WinForms.Helpers;

/// <summary>
/// Coordinates the Z-order of secondary WinForms windows while a parent is in
/// F11 mode. It is intentionally independent from business modules: every
/// visible Form whose Owner chain reaches the active full-screen form receives
/// the same owner-aware activation treatment.
/// </summary>
internal static class FullScreenPresentationCoordinator
{
    private sealed record SecondaryPresentationState(Form ImmediateOwner, Form FullScreenRoot);

    private static readonly Dictionary<Form, SecondaryPresentationState> ManagedSecondaryForms = [];
    private static bool _isWatching;

    internal static void OnFullScreenStateChanged()
    {
        if (FullScreenHelper.HasActiveFullScreenForms)
        {
            if (_isWatching)
            {
                PromoteSecondaryForms();
                return;
            }

            System.Windows.Forms.Application.Idle += ApplicationOnIdle;
            _isWatching = true;
            PromoteSecondaryForms();
            return;
        }

        if (_isWatching)
        {
            System.Windows.Forms.Application.Idle -= ApplicationOnIdle;
            _isWatching = false;
        }

        foreach (var secondary in ManagedSecondaryForms.Keys.ToArray())
        {
            secondary.FormClosed -= SecondaryFormClosed;
        }
        ManagedSecondaryForms.Clear();
    }

    private static void ApplicationOnIdle(object? sender, EventArgs e) => PromoteSecondaryForms();

    private static void PromoteSecondaryForms()
    {
        foreach (Form secondary in System.Windows.Forms.Application.OpenForms)
        {
            if (!secondary.Visible || secondary.IsDisposed || ManagedSecondaryForms.ContainsKey(secondary))
            {
                continue;
            }

            var fullScreenRoot = FullScreenHelper.FindFullScreenOwner(secondary);
            if (fullScreenRoot is null || ReferenceEquals(secondary, fullScreenRoot))
            {
                continue;
            }

            // Give focus back to the immediate owner when one exists. A nested
            // dialog (for example Productos -> Ficha) must not skip its parent
            // and jump directly to the full-screen dashboard when it closes.
            ManagedSecondaryForms.Add(
                secondary,
                new SecondaryPresentationState(secondary.Owner ?? fullScreenRoot, fullScreenRoot));
            secondary.FormClosed += SecondaryFormClosed;

            // Owner is the authority for modality; activation is only used to
            // recover Z-order when the secondary window is created during F11.
            secondary.BringToFront();
            secondary.Activate();
        }
    }

    private static void SecondaryFormClosed(object? sender, FormClosedEventArgs e)
    {
        if (sender is not Form secondary)
        {
            return;
        }

        secondary.FormClosed -= SecondaryFormClosed;
        if (!ManagedSecondaryForms.Remove(secondary, out var presentation) ||
            presentation.FullScreenRoot.IsDisposed ||
            !FullScreenHelper.IsFullScreen(presentation.FullScreenRoot))
        {
            return;
        }

        var immediateOwner = presentation.ImmediateOwner;
        if (immediateOwner.IsDisposed || !immediateOwner.Visible)
        {
            return;
        }

        try
        {
            immediateOwner.BeginInvoke((Action)(() =>
            {
                if (!immediateOwner.IsDisposed && immediateOwner.Visible)
                {
                    immediateOwner.BringToFront();
                    immediateOwner.Activate();
                }
            }));
        }
        catch (InvalidOperationException)
        {
            // The owner can be disposed by an application shutdown between the
            // visibility check and BeginInvoke. No foreground recovery is then needed.
        }
    }
}
