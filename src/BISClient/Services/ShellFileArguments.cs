using System.IO;

namespace BISClient.Services;

internal static class ShellFileArguments
{
    public static IReadOnlyList<string> Parse(IEnumerable<string>? args)
    {
        var files = new List<string>();
        if (args is null)
        {
            return files;
        }

        foreach (var raw in args)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var trimmed = raw.Trim().Trim('"');
            if (trimmed.StartsWith('-') || trimmed.StartsWith('/'))
            {
                continue;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(trimmed);
            }
            catch (Exception)
            {
                continue;
            }

            if (File.Exists(fullPath))
            {
                files.Add(fullPath);
            }
        }

        return files;
    }

    public static List<string> Merge(params IEnumerable<string>[] groups)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<string>();
        foreach (var group in groups)
        {
            foreach (var path in group)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                if (seen.Add(path))
                {
                    files.Add(path);
                }
            }
        }

        return files;
    }
}
