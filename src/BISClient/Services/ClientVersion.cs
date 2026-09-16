using System.Reflection;

namespace BISClient.Services;

internal static class ClientVersion
{
    static ClientVersion()
    {
        var informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        var raw = informational
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "0.0.0";
        var plus = raw.IndexOf('+');
        if (plus >= 0)
        {
            raw = raw[..plus];
        }

        Current = Normalize(TryParse(raw, out var parsed) ? parsed : new Version(0, 0, 0));
        Display = Format(Current);
        UserAgent = $"BISClient/{Display}";
    }

    public static Version Current { get; }

    public static string Display { get; }

    public static string UserAgent { get; }

    public static bool TryParse(string? value, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var raw = value.Trim();
        var plus = raw.IndexOf('+');
        if (plus >= 0)
        {
            raw = raw[..plus];
        }

        var dash = raw.IndexOf('-');
        if (dash >= 0)
        {
            raw = raw[..dash];
        }

        if (!Version.TryParse(raw, out var parsed))
        {
            return false;
        }

        version = Normalize(parsed);
        return true;
    }

    public static bool IsNewer(Version remote, Version local) => Normalize(remote) > Normalize(local);

    public static string Format(Version version)
    {
        var normalized = Normalize(version);
        return $"{normalized.Major}.{normalized.Minor}.{normalized.Build}";
    }

    public static Version Normalize(Version version)
        => new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
}
