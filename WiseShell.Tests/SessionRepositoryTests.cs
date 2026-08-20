using System.Text.Json;
using WiseShell.Core.Enums;
using WiseShell.Core.Infrastructure;
using WiseShell.Core.Models;

namespace WiseShell.Tests;

public sealed class SessionRepositoryTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "WiseShell.Tests", Guid.NewGuid().ToString("N"));
    private static readonly JsonSerializerOptions TestJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsProfiles()
    {
        var repository = new JsonSessionRepository(_tempDirectory);
        var profile = new SessionProfile
        {
            Name = "生产 SSH",
            GroupPath = "Linux/生产",
            Host = "10.0.0.10",
            Port = 2222,
            Username = "root",
            AuthType = AuthenticationType.PrivateKey,
            PrivateKeyPath = @"C:\Keys\id_rsa",
            TerminalTheme = TerminalTheme.Solarized,
            LocalStartupDirectory = @"D:\Workspaces\Web",
            StartupDirectory = "/var/www",
            KeepAliveSeconds = 15,
            FontSize = 15,
            TerminalFontFamily = "Cascadia Mono",
            ScrollbackLines = 8000,
            IsFavorite = true,
            RememberSecret = true,
        };

        await repository.SaveAsync([profile]);

        var loaded = await repository.LoadAsync();
        var loadedProfile = Assert.Single(loaded);

        Assert.Equal(profile.Name, loadedProfile.Name);
        Assert.Equal(profile.GroupPath, loadedProfile.GroupPath);
        Assert.Equal(profile.Host, loadedProfile.Host);
        Assert.Equal(profile.Port, loadedProfile.Port);
        Assert.Equal(profile.Username, loadedProfile.Username);
        Assert.Equal(profile.AuthType, loadedProfile.AuthType);
        Assert.Equal(profile.PrivateKeyPath, loadedProfile.PrivateKeyPath);
        Assert.Equal(profile.TerminalTheme, loadedProfile.TerminalTheme);
        Assert.Equal(profile.LocalStartupDirectory, loadedProfile.LocalStartupDirectory);
        Assert.Equal(profile.StartupDirectory, loadedProfile.StartupDirectory);
        Assert.Equal(profile.KeepAliveSeconds, loadedProfile.KeepAliveSeconds);
        Assert.Equal(profile.FontSize, loadedProfile.FontSize);
        Assert.Equal(profile.TerminalFontFamily, loadedProfile.TerminalFontFamily);
        Assert.Equal(profile.ScrollbackLines, loadedProfile.ScrollbackLines);
        Assert.True(loadedProfile.IsFavorite);
        Assert.True(loadedProfile.RememberSecret);
    }

    [Fact]
    public async Task SaveWorkspaceAsync_RoundTripsFolders()
    {
        var repository = new JsonSessionRepository(_tempDirectory);
        var workspace = new SessionWorkspace
        {
            Profiles =
            [
                new SessionProfile
                {
                    Name = "Ollama",
                    GroupPath = "AI/LLM",
                    Host = "14.18.16.24",
                    Username = "kfb",
                    TerminalFontFamily = "Consolas",
                },
            ],
            FolderPaths = ["AI", "AI/LLM", "ESXi", "ESXi/Lab"],
            TerminalDefaults = new TerminalDefaultsSettings
            {
                FontSize = 16,
                FontFamily = "Cascadia Mono",
            },
        };

        await repository.SaveWorkspaceAsync(workspace);

        var loaded = await repository.LoadWorkspaceAsync();

        Assert.Single(loaded.Profiles);
        Assert.Contains("ESXi", loaded.FolderPaths);
        Assert.Contains("ESXi/Lab", loaded.FolderPaths);
        Assert.Contains("AI/LLM", loaded.FolderPaths);
        Assert.Equal(16, loaded.TerminalDefaults.FontSize);
        Assert.Equal("Cascadia Mono", loaded.TerminalDefaults.FontFamily);
    }

    [Fact]
    public async Task LoadWorkspaceAsync_SupportsLegacyArrayFormat()
    {
        var sessionsFile = AppDataPaths.GetSessionsFile(_tempDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(sessionsFile)!);

        await File.WriteAllTextAsync(
            sessionsFile,
            JsonSerializer.Serialize(
                new[]
                {
                    new SessionProfile
                    {
                        Name = "Legacy",
                        GroupPath = @"Prod\Linux",
                        Host = "192.168.0.8",
                        Username = "root",
                    },
                },
                TestJson));

        var repository = new JsonSessionRepository(_tempDirectory);
        var loaded = await repository.LoadWorkspaceAsync();

        var profile = Assert.Single(loaded.Profiles);
        Assert.Equal("Legacy", profile.Name);
        Assert.Empty(loaded.FolderPaths);
        Assert.Equal(TerminalDefaultsSettings.DefaultFontSize, loaded.TerminalDefaults.FontSize);
        Assert.Equal(TerminalDefaultsSettings.DefaultFontFamily, loaded.TerminalDefaults.FontFamily);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
