namespace WiseShell.Core.Infrastructure;

public static class RemotePathHelper
{
    public static string NormalizeAbsolute(string? path, string fallback = "/")
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return NormalizeRooted(fallback);
        }

        return NormalizeRooted(path);
    }

    public static string Resolve(string basePath, string? path)
    {
        var normalizedBase = NormalizeRooted(string.IsNullOrWhiteSpace(basePath) ? "/" : basePath);
        if (string.IsNullOrWhiteSpace(path) || path == ".")
        {
            return normalizedBase;
        }

        var candidate = path.Replace('\\', '/');
        if (candidate.StartsWith('/'))
        {
            return NormalizeRooted(candidate);
        }

        return NormalizeRooted($"{normalizedBase.TrimEnd('/')}/{candidate}");
    }

    public static string Combine(string currentPath, string childName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(childName);

        return Resolve(currentPath, childName);
    }

    public static string GetParent(string path)
    {
        var normalized = NormalizeRooted(path);
        if (normalized == "/")
        {
            return "/";
        }

        var splitIndex = normalized.LastIndexOf('/');
        return splitIndex <= 0 ? "/" : normalized[..splitIndex];
    }

    private static string NormalizeRooted(string path)
    {
        var candidate = (path ?? string.Empty).Replace('\\', '/');
        var segments = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var stack = new List<string>(segments.Length);

        foreach (var rawSegment in segments)
        {
            var segment = rawSegment.Trim();
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (stack.Count > 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                continue;
            }

            stack.Add(segment);
        }

        return stack.Count == 0
            ? "/"
            : $"/{string.Join('/', stack)}";
    }
}
