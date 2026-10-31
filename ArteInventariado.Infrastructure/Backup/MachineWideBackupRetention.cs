using System.Globalization;
using ArteInventariado.Application.Models;

namespace ArteInventariado.Infrastructure.Backup;

public enum BackupGenerationKind
{
    Daily = 1,
    Weekly = 2,
    Monthly = 3
}

public sealed record AutomaticBackupCandidate(string FilePath, BackupManifest Manifest);

public sealed record MachineWideBackupRetentionPlan(
    IReadOnlySet<string> KeptFiles,
    IReadOnlyList<string> DeletableFiles,
    int DailyGenerationsKept,
    int WeeklyGenerationsKept,
    int MonthlyGenerationsKept)
{
    public bool Keeps(string filePath) => KeptFiles.Contains(filePath);
}

/// <summary>
/// Deterministic generation-based retention. A generation is the newest
/// valid automatic backup in a UTC calendar day, ISO week or UTC month.
/// The union of the three policies is retained, plus the newest valid file.
/// </summary>
public static class MachineWideBackupRetention
{
    public static MachineWideBackupRetentionPlan BuildPlan(
        IEnumerable<AutomaticBackupCandidate> candidates,
        MachineWideBackupPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(policy);

        var valid = candidates
            .Where(candidate => candidate.Manifest.Kind == Application.Models.BackupKind.Automatic)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.FilePath))
            .OrderByDescending(candidate => candidate.Manifest.CreatedUtc)
            .ThenByDescending(candidate => candidate.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var kept = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (valid.Count > 0)
        {
            // The newest recoverable package is never deleted, even if a
            // future policy is accidentally configured with no generations.
            kept.Add(valid[0].FilePath);
        }

        // A degraded asset package is still useful for recovering the
        // database, but it must never displace the newest fully healthy
        // package. Keep that healthy generation outside the normal period
        // calculation so a newer degraded run cannot delete it.
        var newestHealthy = valid.FirstOrDefault(candidate => IsHealthy(candidate.Manifest));
        if (newestHealthy is not null)
        {
            kept.Add(newestHealthy.FilePath);
        }

        var daily = KeepLatestPeriods(
            valid,
            policy.DailyGenerations,
            candidate => candidate.Manifest.CreatedUtc.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            kept);
        var weekly = KeepLatestPeriods(
            valid,
            policy.WeeklyGenerations,
            candidate =>
            {
                var date = DateOnly.FromDateTime(candidate.Manifest.CreatedUtc.UtcDateTime.Date);
                return $"{ISOWeek.GetYear(date.ToDateTime(TimeOnly.MinValue))}-W{ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue)):00}";
            },
            kept);
        var monthly = KeepLatestPeriods(
            valid,
            policy.MonthlyGenerations,
            candidate => candidate.Manifest.CreatedUtc.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            kept);

        var deletable = valid
            .Select(candidate => candidate.FilePath)
            .Where(path => !kept.Contains(path))
            .ToList();
        return new MachineWideBackupRetentionPlan(
            kept,
            deletable,
            daily,
            weekly,
            monthly);
    }

    private static bool IsHealthy(BackupManifest manifest) =>
        manifest.ContentStatus == BackupContentStatus.Healthy &&
        !manifest.Assets.Any(asset => asset.WasMissing);

    private static int KeepLatestPeriods(
        IReadOnlyList<AutomaticBackupCandidate> candidates,
        int requestedGenerations,
        Func<AutomaticBackupCandidate, string> keySelector,
        ISet<string> kept)
    {
        var count = Math.Max(1, requestedGenerations);
        var periods = candidates
            .GroupBy(keySelector, StringComparer.Ordinal)
            .OrderByDescending(group => group.Max(candidate => candidate.Manifest.CreatedUtc))
            .Take(count)
            .ToList();
        foreach (var period in periods)
        {
            var newest = period
                .OrderByDescending(candidate => candidate.Manifest.CreatedUtc)
                .ThenByDescending(candidate => candidate.FilePath, StringComparer.OrdinalIgnoreCase)
                .First();
            kept.Add(newest.FilePath);
        }

        return periods.Count;
    }
}
