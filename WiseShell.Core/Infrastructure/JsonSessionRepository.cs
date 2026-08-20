using System.Text.Json;
using WiseShell.Core.Interfaces;
using WiseShell.Core.Models;

namespace WiseShell.Core.Infrastructure;

public sealed class JsonSessionRepository : ISessionRepository
{
    private readonly string _sessionsFile;

    public JsonSessionRepository(string? baseDirectory = null)
    {
        _sessionsFile = AppDataPaths.GetSessionsFile(baseDirectory);
    }

    public async Task<SessionWorkspace> LoadWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_sessionsFile))
        {
            return new SessionWorkspace();
        }

        await using var stream = File.OpenRead(_sessionsFile);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            var legacyProfiles = document.RootElement.Deserialize<List<SessionProfile>>(JsonOptionsProvider.Json) ?? new List<SessionProfile>();
            return CreateWorkspace(legacyProfiles, [], new TerminalDefaultsSettings());
        }

        var repositoryDocument = document.RootElement.Deserialize<SessionWorkspaceDocument>(JsonOptionsProvider.Json);
        return CreateWorkspace(
            repositoryDocument?.Sessions ?? [],
            repositoryDocument?.Folders ?? [],
            repositoryDocument?.TerminalDefaults);
    }

    public async Task<IReadOnlyList<SessionProfile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var workspace = await LoadWorkspaceAsync(cancellationToken);
        return workspace.Profiles;
    }

    public async Task SaveWorkspaceAsync(SessionWorkspace workspace, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        Directory.CreateDirectory(Path.GetDirectoryName(_sessionsFile)!);
        await using var stream = File.Create(_sessionsFile);

        var document = new SessionWorkspaceDocument
        {
            Sessions = workspace.Profiles
                .OrderBy(profile => SessionFolderPath.Normalize(profile.GroupPath), StringComparer.OrdinalIgnoreCase)
                .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Folders = workspace.FolderPaths
                .Select(SessionFolderPath.Normalize)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            TerminalDefaults = workspace.TerminalDefaults.CreateCopy(),
        };

        await JsonSerializer.SerializeAsync(stream, document, JsonOptionsProvider.Json, cancellationToken);
    }

    public Task SaveAsync(IReadOnlyCollection<SessionProfile> profiles, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        return SaveWorkspaceAsync(new SessionWorkspace
        {
            Profiles = profiles.ToList(),
            FolderPaths = [],
            TerminalDefaults = new TerminalDefaultsSettings(),
        }, cancellationToken);
    }

    private static SessionWorkspace CreateWorkspace(
        IReadOnlyCollection<SessionProfile> profiles,
        IReadOnlyCollection<string> folderPaths,
        TerminalDefaultsSettings? terminalDefaults)
    {
        var normalizedDefaults = NormalizeTerminalDefaults(terminalDefaults);
        var normalizedProfiles = profiles
            .Select(profile => NormalizeProfile(profile, normalizedDefaults))
            .OrderBy(profile => SessionFolderPath.Normalize(profile.GroupPath), StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new SessionWorkspace
        {
            Profiles = normalizedProfiles,
            FolderPaths = folderPaths
                .Select(SessionFolderPath.Normalize)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            TerminalDefaults = normalizedDefaults,
        };
    }

    private static TerminalDefaultsSettings NormalizeTerminalDefaults(TerminalDefaultsSettings? terminalDefaults)
    {
        var normalized = terminalDefaults?.CreateCopy() ?? new TerminalDefaultsSettings();
        if (normalized.FontSize < 9d)
        {
            normalized.FontSize = TerminalDefaultsSettings.DefaultFontSize;
        }

        normalized.FontFamily = string.IsNullOrWhiteSpace(normalized.FontFamily)
            ? TerminalDefaultsSettings.DefaultFontFamily
            : normalized.FontFamily.Trim();
        return normalized;
    }

    private static SessionProfile NormalizeProfile(SessionProfile profile, TerminalDefaultsSettings terminalDefaults)
    {
        profile.GroupPath = SessionFolderPath.Normalize(profile.GroupPath);
        if (profile.FontSize < 9d)
        {
            profile.FontSize = terminalDefaults.FontSize;
        }

        profile.TerminalFontFamily = string.IsNullOrWhiteSpace(profile.TerminalFontFamily)
            ? terminalDefaults.FontFamily
            : profile.TerminalFontFamily.Trim();
        return profile;
    }

    private sealed class SessionWorkspaceDocument
    {
        public List<SessionProfile> Sessions { get; set; } = [];

        public List<string> Folders { get; set; } = [];

        public TerminalDefaultsSettings TerminalDefaults { get; set; } = new();
    }
}
