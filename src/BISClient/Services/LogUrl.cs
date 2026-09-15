using System.Text;

namespace BISClient.Services;

internal static class LogUrl
{
    public static string Sanitize(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return "(leer)";
        }

        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
        {
            return "(ungueltig)";
        }

        var builder = new StringBuilder();
        builder.Append(parsed.Scheme);
        builder.Append("://");
        builder.Append(parsed.IdnHost);
        if (!parsed.IsDefaultPort)
        {
            builder.Append(':');
            builder.Append(parsed.Port);
        }

        builder.Append(parsed.AbsolutePath);
        return builder.ToString();
    }
}
