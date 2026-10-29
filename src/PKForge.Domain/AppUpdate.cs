namespace PKForge.Domain;

/// <summary>The latest installable release discovered by the app.</summary>
public sealed record AvailableAppUpdate(
    string Version,
    string ReleaseUrl,
    string DownloadUrl,
    long SizeBytes);

/// <summary>Result of comparing the installed app with GitHub's latest release.</summary>
public sealed record AppUpdateCheck(
    bool IsAvailable,
    AvailableAppUpdate? Update,
    string Message);

public enum AppUpdateInstallResult
{
    InstallerOpened,
    InstallPermissionRequired,
    ReleasePageOpened,
}

/// <summary>Pure release-channel rules. Network and platform services stay in the app layer.</summary>
public static class AppUpdateRules
{
    /// <summary>Standard release tags are vMAJOR.MINOR.PATCH; build metadata is ignored.</summary>
    public static bool IsNewerVersion(string installed, string candidate)
    {
        if (!TryParse(installed, out var current) || !TryParse(candidate, out var next))
            return false;
        var core = next.Core.CompareTo(current.Core);
        if (core != 0)
            return core > 0;
        // SemVer: a release outranks any prerelease of the same version; among two
        // prereleases, the one with the more dot-separated identifiers wins when all
        // shared identifiers tie (1.4.0-beta.2 > 1.4.0-beta.1 > 1.4.0-beta).
        if (next.Prerelease.Length == 0 || current.Prerelease.Length == 0)
            return next.Prerelease.Length == 0 && current.Prerelease.Length != 0;
        return ComparePrerelease(next.Prerelease, current.Prerelease) > 0;
    }

    /// <summary>Don't-remind-me applies to one release. A newer release always asks again.</summary>
    public static bool ShouldPromptAutomatically(string installed, string candidate, string skipped) =>
        IsNewerVersion(installed, candidate) && !string.Equals(candidate, skipped, StringComparison.OrdinalIgnoreCase);

    private static bool TryParse(string value, out ReleaseVersion version)
    {
        var prerelease = "";
        var text = value.Trim();
        if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            text = text[1..];
        var plus = text.IndexOf('+');
        if (plus >= 0)
            text = text[..plus];
        // PKForge release tags are stable SemVer. Strip prerelease metadata if a future
        // tag carries it rather than treating the whole tag as incomparable.
        var dash = text.IndexOf('-');
        if (dash >= 0)
        {
            prerelease = text[(dash + 1)..];
            text = text[..dash];
        }
        if (!Version.TryParse(text, out var core))
        {
            version = default;
            return false;
        }
        version = new ReleaseVersion(core, prerelease);
        return true;
    }

    /// <summary>SemVer prerelease ordering: numeric identifiers compare numerically and
    /// rank below alphanumeric ones; more identifiers win when the shared prefix ties.</summary>
    private static int ComparePrerelease(string left, string right)
    {
        var a = left.Split('.');
        var b = right.Split('.');
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var aNumeric = int.TryParse(a[i], out var an);
            var bNumeric = int.TryParse(b[i], out var bn);
            int order;
            if (aNumeric && bNumeric)
                order = an.CompareTo(bn);
            else if (aNumeric)
                order = -1; // numeric identifiers rank lower than alphanumeric ones
            else if (bNumeric)
                order = 1;
            else
                order = string.CompareOrdinal(a[i], b[i]);
            if (order != 0) return order;
        }
        return a.Length.CompareTo(b.Length);
    }

    private readonly record struct ReleaseVersion(Version Core, string Prerelease);
}
