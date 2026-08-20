namespace WiseShell.Core.Models;

public sealed class SessionWorkspace
{
    public IReadOnlyList<SessionProfile> Profiles { get; init; } = Array.Empty<SessionProfile>();

    public IReadOnlyList<string> FolderPaths { get; init; } = Array.Empty<string>();

    public TerminalDefaultsSettings TerminalDefaults { get; init; } = new();
}
