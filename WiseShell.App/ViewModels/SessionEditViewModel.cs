using WiseShell.App.Infrastructure;
using WiseShell.Core.Enums;
using WiseShell.Core.Models;

namespace WiseShell.App.ViewModels;

public sealed class SessionEditViewModel : ObservableObject
{
    private string _name = string.Empty;
    private string _groupPath = string.Empty;
    private string _host = string.Empty;
    private int _port = 22;
    private string _username = string.Empty;
    private AuthenticationType _authType;
    private string _privateKeyPath = string.Empty;
    private TerminalTheme _terminalTheme;
    private string _localStartupDirectory = string.Empty;
    private string _startupDirectory = string.Empty;
    private int _keepAliveSeconds = 30;
    private double _fontSize = TerminalDefaultsSettings.DefaultFontSize;
    private string _terminalFontFamily = string.Empty;
    private int _scrollbackLines = 5000;
    private bool _isFavorite;
    private bool _rememberSecret;

    public SessionEditViewModel(SessionProfile profile, bool hasStoredSecret)
    {
        Id = profile.Id;
        Title = profile.Name;
        HasStoredSecret = hasStoredSecret;

        _name = profile.Name;
        _groupPath = profile.GroupPath;
        _host = profile.Host;
        _port = profile.Port;
        _username = profile.Username;
        _authType = profile.AuthType;
        _privateKeyPath = profile.PrivateKeyPath;
        _terminalTheme = profile.TerminalTheme;
        _localStartupDirectory = profile.LocalStartupDirectory;
        _startupDirectory = profile.StartupDirectory;
        _keepAliveSeconds = profile.KeepAliveSeconds;
        _fontSize = profile.FontSize;
        _terminalFontFamily = profile.TerminalFontFamily;
        _scrollbackLines = profile.ScrollbackLines;
        _isFavorite = profile.IsFavorite;
        _rememberSecret = profile.RememberSecret;
    }

    public Guid Id { get; }

    public string Title { get; }

    public bool HasStoredSecret { get; }

    public bool IsPrivateKeyAuth => AuthType == AuthenticationType.PrivateKey;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string GroupPath
    {
        get => _groupPath;
        set => SetProperty(ref _groupPath, value);
    }

    public string Host
    {
        get => _host;
        set => SetProperty(ref _host, value);
    }

    public int Port
    {
        get => _port;
        set => SetProperty(ref _port, value);
    }

    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    public AuthenticationType AuthType
    {
        get => _authType;
        set
        {
            if (SetProperty(ref _authType, value))
            {
                OnPropertyChanged(nameof(IsPrivateKeyAuth));
            }
        }
    }

    public string PrivateKeyPath
    {
        get => _privateKeyPath;
        set => SetProperty(ref _privateKeyPath, value);
    }

    public TerminalTheme TerminalTheme
    {
        get => _terminalTheme;
        set => SetProperty(ref _terminalTheme, value);
    }

    public string LocalStartupDirectory
    {
        get => _localStartupDirectory;
        set => SetProperty(ref _localStartupDirectory, value);
    }

    public string StartupDirectory
    {
        get => _startupDirectory;
        set => SetProperty(ref _startupDirectory, value);
    }

    public int KeepAliveSeconds
    {
        get => _keepAliveSeconds;
        set => SetProperty(ref _keepAliveSeconds, value);
    }

    public double FontSize
    {
        get => _fontSize;
        set => SetProperty(ref _fontSize, value);
    }

    public string TerminalFontFamily
    {
        get => _terminalFontFamily;
        set => SetProperty(ref _terminalFontFamily, value);
    }

    public int ScrollbackLines
    {
        get => _scrollbackLines;
        set => SetProperty(ref _scrollbackLines, value);
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetProperty(ref _isFavorite, value);
    }

    public bool RememberSecret
    {
        get => _rememberSecret;
        set => SetProperty(ref _rememberSecret, value);
    }

    public SessionProfile ToProfile()
    {
        return new SessionProfile
        {
            Id = Id,
            Name = Name.Trim(),
            GroupPath = GroupPath.Trim(),
            Host = Host.Trim(),
            Port = Port,
            Username = Username.Trim(),
            AuthType = AuthType,
            PrivateKeyPath = PrivateKeyPath.Trim(),
            TerminalTheme = TerminalTheme,
            LocalStartupDirectory = LocalStartupDirectory.Trim(),
            StartupDirectory = StartupDirectory.Trim(),
            KeepAliveSeconds = KeepAliveSeconds,
            FontSize = FontSize,
            TerminalFontFamily = TerminalFontFamily.Trim(),
            ScrollbackLines = ScrollbackLines,
            IsFavorite = IsFavorite,
            RememberSecret = RememberSecret,
        };
    }
}
