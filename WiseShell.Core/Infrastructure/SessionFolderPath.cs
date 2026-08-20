namespace WiseShell.Core.Infrastructure;

public static class SessionFolderPath
{
    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var segments = path
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

        return segments.Length == 0 ? string.Empty : string.Join('/', segments);
    }

    public static string Combine(string? parentPath, string? childName)
    {
        var parent = Normalize(parentPath);
        var child = Normalize(childName);

        if (string.IsNullOrEmpty(parent))
        {
            return child;
        }

        if (string.IsNullOrEmpty(child))
        {
            return parent;
        }

        return $"{parent}/{child}";
    }

    public static string GetParent(string? path)
    {
        var normalized = Normalize(path);
        if (string.IsNullOrEmpty(normalized))
        {
            return string.Empty;
        }

        var separatorIndex = normalized.LastIndexOf('/');
        return separatorIndex < 0 ? string.Empty : normalized[..separatorIndex];
    }

    public static string GetName(string? path)
    {
        var normalized = Normalize(path);
        if (string.IsNullOrEmpty(normalized))
        {
            return string.Empty;
        }

        var separatorIndex = normalized.LastIndexOf('/');
        return separatorIndex < 0 ? normalized : normalized[(separatorIndex + 1)..];
    }

    public static bool IsSameOrDescendant(string? path, string? folderPath)
    {
        var normalizedPath = Normalize(path);
        var normalizedFolder = Normalize(folderPath);

        if (string.IsNullOrEmpty(normalizedFolder))
        {
            return true;
        }

        return normalizedPath.Equals(normalizedFolder, StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.StartsWith(normalizedFolder + "/", StringComparison.OrdinalIgnoreCase);
    }

    public static string ReplacePrefix(string? path, string? oldPrefix, string? newPrefix)
    {
        var normalizedPath = Normalize(path);
        var normalizedOldPrefix = Normalize(oldPrefix);
        var normalizedNewPrefix = Normalize(newPrefix);

        if (string.IsNullOrEmpty(normalizedOldPrefix) || !IsSameOrDescendant(normalizedPath, normalizedOldPrefix))
        {
            return normalizedPath;
        }

        if (normalizedPath.Equals(normalizedOldPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedNewPrefix;
        }

        var suffix = normalizedPath[(normalizedOldPrefix.Length + 1)..];
        return Combine(normalizedNewPrefix, suffix);
    }
}
