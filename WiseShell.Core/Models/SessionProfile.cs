using WiseShell.Core.Enums;

namespace WiseShell.Core.Models;

public sealed class SessionProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "新建会话";

    public string GroupPath { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 22;

    public string Username { get; set; } = string.Empty;

    public AuthenticationType AuthType { get; set; } = AuthenticationType.Password;

    public string PrivateKeyPath { get; set; } = string.Empty;

    public TerminalTheme TerminalTheme { get; set; } = TerminalTheme.Dark;

    public string LocalStartupDirectory { get; set; } = string.Empty;

    public string StartupDirectory { get; set; } = string.Empty;

    public int KeepAliveSeconds { get; set; } = 30;

    public double FontSize { get; set; } = TerminalDefaultsSettings.DefaultFontSize;

    public string TerminalFontFamily { get; set; } = TerminalDefaultsSettings.DefaultFontFamily;

    public int ScrollbackLines { get; set; } = 5000;

    public bool IsFavorite { get; set; }

    public bool RememberSecret { get; set; }

    public SessionProfile CreateDuplicate()
    {
        return new SessionProfile
        {
            Id = Guid.NewGuid(),
            Name = $"{Name} 副本",
            GroupPath = GroupPath,
            Host = Host,
            Port = Port,
            Username = Username,
            AuthType = AuthType,
            PrivateKeyPath = PrivateKeyPath,
            TerminalTheme = TerminalTheme,
            LocalStartupDirectory = LocalStartupDirectory,
            StartupDirectory = StartupDirectory,
            KeepAliveSeconds = KeepAliveSeconds,
            FontSize = FontSize,
            TerminalFontFamily = TerminalFontFamily,
            ScrollbackLines = ScrollbackLines,
            IsFavorite = IsFavorite,
            RememberSecret = RememberSecret,
        };
    }
}
