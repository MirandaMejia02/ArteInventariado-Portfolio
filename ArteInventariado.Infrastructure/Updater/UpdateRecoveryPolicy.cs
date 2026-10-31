using ArteInventariado.Application.Models;

namespace ArteInventariado.Infrastructure.Updater;

public static class UpdateRecoveryPolicy
{
    public static bool RequiresRecovery(UpdateStateDocument? state) =>
        state?.State is
            UpdateState.Preparing or
            UpdateState.BackupCreated or
            UpdateState.Installing or
            UpdateState.Verifying or
            UpdateState.RollbackRequired or
            UpdateState.RollingBack;

    public static bool RequiresRecovery(
        UpdateStateDocument? state,
        UpdateVersionPointer? current)
    {
        if (!RequiresRecovery(state))
        {
            return false;
        }

        if (state is null || current is null)
        {
            return true;
        }

        // A protected state from an older transaction must not force recovery
        // after CURRENT has already committed a later valid target.  The
        // updater still recovers an interrupted transaction while CURRENT is
        // the source version (the normal pre-install situation).
        if (UpdateStateSemantics.IsRollback(state) &&
            IsCurrentAtOrBeyondTarget(current.Version, state) &&
            !string.Equals(current.Version, state.SourceVersion, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool IsCurrentAtOrBeyondTarget(
        string currentVersion,
        UpdateStateDocument state)
    {
        return UpdateVersion.TryParse(currentVersion, out var current) &&
               UpdateVersion.TryParse(state.TargetVersion, out var target) &&
               current.CompareTo(target) >= 0;
    }
}
