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

        if (parsed.Scheme.Equals("data", StringComparison.OrdinalIgnoreCase))
        {
            return "data:";
        }

        if (!parsed.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !parsed.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            var opaque = parsed.GetLeftPart(UriPartial.Path);
            return string.IsNullOrWhiteSpace(opaque) ? parsed.Scheme : opaque;
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
