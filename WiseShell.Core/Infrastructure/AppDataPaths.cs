namespace WiseShell.Core.Infrastructure;

public static class AppDataPaths
{
    public static string GetAppDirectory(string? baseDirectory = null)
    {
        var root = baseDirectory;
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WiseShell");
        }

        Directory.CreateDirectory(root);
        return root;
    }

    public static string GetSessionsFile(string? baseDirectory = null)
        => Path.Combine(GetAppDirectory(baseDirectory), "sessions.json");

    public static string GetCredentialsFile(string? baseDirectory = null)
        => Path.Combine(GetAppDirectory(baseDirectory), "credentials.json");

    public static string GetKnownHostsFile(string? baseDirectory = null)
        => Path.Combine(GetAppDirectory(baseDirectory), "known-hosts.json");

    public static string GetLogsDirectory(string? baseDirectory = null)
    {
        var path = Path.Combine(GetAppDirectory(baseDirectory), "Logs");
        Directory.CreateDirectory(path);
        return path;
    }
}
