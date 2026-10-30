namespace AbleBounce;

public static class LiveCompatibility
{
    public static bool IsSupported(string? version) =>
        System.Version.TryParse(version, out var parsed)
        && parsed.Major == 12 && parsed.Minor == 4
        && parsed.Build >= 0 && parsed.Revision == -1;
}
