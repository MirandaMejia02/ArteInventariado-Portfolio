namespace ArteInventariado.Infrastructure.Updater;

public readonly record struct UpdateVersion(int Major, int Minor, int Patch, string? PreRelease = null) : IComparable<UpdateVersion>
{
    public static UpdateVersion Parse(string value)
    {
        if (!TryParse(value, out var result))
            throw new FormatException($"La version '{value}' no tiene un formato semver valido.");
        return result;
    }

    public static bool TryParse(string? value, out UpdateVersion result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var trimmed = value.Trim().TrimStart('v', 'V');
        var plus = trimmed.IndexOf('+');
        if (plus >= 0) trimmed = trimmed[..plus];
        var dash = trimmed.IndexOf('-');
        var core = dash >= 0 ? trimmed[..dash] : trimmed;
        var pre = dash >= 0 ? trimmed[(dash + 1)..] : null;
        var parts = core.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var major) ||
            !int.TryParse(parts[1], out var minor) || !int.TryParse(parts[2], out var patch) ||
            major < 0 || minor < 0 || patch < 0 || (pre is not null && string.IsNullOrWhiteSpace(pre)))
            return false;
        result = new UpdateVersion(major, minor, patch, pre);
        return true;
    }

    public int CompareTo(UpdateVersion other)
    {
        var core = Major.CompareTo(other.Major);
        if (core != 0) return core;
        core = Minor.CompareTo(other.Minor);
        if (core != 0) return core;
        core = Patch.CompareTo(other.Patch);
        if (core != 0) return core;
        if (string.Equals(PreRelease, other.PreRelease, StringComparison.OrdinalIgnoreCase)) return 0;
        if (PreRelease is null) return 1;
        if (other.PreRelease is null) return -1;
        return ComparePrerelease(PreRelease, other.PreRelease);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}{(string.IsNullOrWhiteSpace(PreRelease) ? string.Empty : $"-{PreRelease}")}";

    public static string Sanitize(string value)
    {
        var parsed = Parse(value);
        return parsed.ToString();
    }

    private static int ComparePrerelease(string left, string right)
    {
        var a = left.Split('.');
        var b = right.Split('.');
        for (var index = 0; index < Math.Max(a.Length, b.Length); index++)
        {
            if (index >= a.Length) return -1;
            if (index >= b.Length) return 1;
            var ai = int.TryParse(a[index], out var an);
            var bi = int.TryParse(b[index], out var bn);
            if (ai && bi) { var c = an.CompareTo(bn); if (c != 0) return c; }
            else if (ai != bi) return ai ? -1 : 1;
            else { var c = string.CompareOrdinal(a[index], b[index]); if (c != 0) return c; }
        }
        return 0;
    }
}
