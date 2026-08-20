using System.IO;
using System.Text;
using System.Windows.Input;
using System.Windows.Threading;
using WiseShell.App.Infrastructure;
using WiseShell.Core.Enums;
using WiseShell.Core.Infrastructure;
using WiseShell.Core.Interfaces;
using WiseShell.Core.Models;

namespace WiseShell.App.ViewModels;

public sealed class TerminalTabViewModel : ObservableObject, IAsyncDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Func<ITerminalSession> _sessionFactory;
    private readonly Func<CancellationToken, Task<TerminalSessionStartRequest>> _requestFactory;
    private readonly Func<TerminalTabViewModel, Task> _closeCallback;
    private readonly Action<TerminalSessionStartRequest>? _connectedCallback;
    private readonly object _outputSync = new();
    private readonly object _logSync = new();
    private readonly StringBuilder _outputBuffer = new();
    private ITerminalSession? _session;
    private TerminalSessionStartRequest? _lastConnectedRequest;
    private StreamWriter? _logWriter;
    private string _header;
    private string _statusText = "未连接";
    private string _terminalSizeText = "终端尺寸: -- x --";
    private string _logStatusText = "日志: 未启用";
    private string? _customHeader;
    private TerminalConnectionState _connectionState = TerminalConnectionState.Disconnected;
    private bool _disposed;

    public TerminalTabViewModel(
        SessionProfile profile,
        Dispatcher dispatcher,
        Func<ITerminalSession> sessionFactory,
        Func<CancellationToken, Task<TerminalSessionStartRequest>> requestFactory,
        Func<TerminalTabViewModel, Task> closeCallback,
        Action<TerminalSessionStartRequest>? connectedCallback = null)
    {
        Profile = profile;
        _dispatcher = dispatcher;
        _sessionFactory = sessionFactory;
        _requestFactory = requestFactory;
        _closeCallback = closeCallback;
        _connectedCallback = connectedCallback;
        _header = profile.Name;

        CloseCommand = new AsyncRelayCommand(() => _closeCallback(this));
    }

    public event Action<string>? OutputAppended;

    public SessionProfile Profile { get; }

    public ICommand CloseCommand { get; }

    public string Header
    {
        get => _header;
        private set => SetProperty(ref _header, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string TerminalSizeText
    {
        get => _terminalSizeText;
        private set => SetProperty(ref _terminalSizeText, value);
    }

    public string LogStatusText
    {
        get => _logStatusText;
        private set => SetProperty(ref _logStatusText, value);
    }

    public TerminalTheme Theme => Profile.TerminalTheme;

    public double FontSize => Math.Clamp(Profile.FontSize, 9d, 36d);

    public string FontFamily => TerminalFontCatalog.NormalizeFontFamily(Profile.TerminalFontFamily);

    public int ScrollbackLines => Profile.ScrollbackLines;

    public string SessionAddress => $"{Profile.Username}@{Profile.Host}:{Profile.Port}";

    public bool IsConnected => _connectionState == TerminalConnectionState.Connected;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await ConnectCoreAsync(cancellationToken);
    }

    public async Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        await CloseAsync(cancellationToken, changeStatus: false);
        await ConnectCoreAsync(cancellationToken);
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default, bool changeStatus = true)
    {
        if (_disposed)
        {
            return;
        }

        if (_session is not null)
        {
            await _session.CloseAsync(cancellationToken);
            await _session.DisposeAsync();
            _session = null;
        }

        UpdateConnectionState(TerminalConnectionState.Disconnected);
        _lastConnectedRequest = null;

        var writerToDispose = DetachLogWriter();
        if (writerToDispose is not null)
        {
            await writerToDispose.DisposeAsync();
        }

        if (changeStatus)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                StatusText = "连接已关闭";
                LogStatusText = "日志: 已关闭";
            });
        }
    }

    public async Task HandleWebMessageAsync(TerminalWebMessage message, CancellationToken cancellationToken = default)
    {
        if (_session is null)
        {
            return;
        }

        switch (message.Type)
        {
            case TerminalWebMessageType.Input:
                if (!string.IsNullOrEmpty(message.Text))
                {
                    await _session.SendInputAsync(message.Text, cancellationToken);
                }

                break;
            case TerminalWebMessageType.Resize:
                if (message.Columns.HasValue && message.Rows.HasValue)
                {
                    TerminalSizeText = $"终端尺寸: {message.Columns} x {message.Rows}";
                    await _session.ResizeAsync(message.Columns.Value, message.Rows.Value, cancellationToken);
                }

                break;
        }
    }

    public void OverrideHeader(string value)
    {
        _customHeader = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        RefreshHeader();
    }

    public void RefreshHeader()
    {
        Header = _customHeader ?? Profile.Name;
    }

    public void NotifyAppearanceChanged()
    {
        OnPropertyChanged(nameof(Theme));
        OnPropertyChanged(nameof(FontSize));
        OnPropertyChanged(nameof(FontFamily));
        OnPropertyChanged(nameof(ScrollbackLines));
    }

    public string GetBufferedOutput()
    {
        lock (_outputSync)
        {
            return _outputBuffer.ToString();
        }
    }

    public bool TryCreateSftpConnectionRequest(out SftpConnectionRequest? request)
    {
        if (!IsConnected || _lastConnectedRequest is null)
        {
            request = null;
            return false;
        }

        request = new SftpConnectionRequest
        {
            Profile = CloneProfile(_lastConnectedRequest.Profile),
            Secret = _lastConnectedRequest.Secret,
            HostKeyVerificationCallback = _lastConnectedRequest.HostKeyVerificationCallback,
        };

        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await CloseAsync(changeStatus: false);
    }

    private async Task ConnectCoreAsync(CancellationToken cancellationToken)
    {
        var request = await _requestFactory(cancellationToken);
        var session = _sessionFactory();
        session.OutputReceived += OnSessionOutputReceived;
        session.ConnectionStateChanged += OnSessionConnectionStateChanged;

        _session = session;
        OpenLogWriter();
        await session.ConnectAsync(request, cancellationToken);
        _lastConnectedRequest = CloneRequest(request);
        UpdateConnectionState(TerminalConnectionState.Connected);
        _connectedCallback?.Invoke(request);
    }

    private async void OnSessionConnectionStateChanged(object? sender, TerminalConnectionStateChangedEventArgs e)
    {
        UpdateConnectionState(e.State);
        await _dispatcher.InvokeAsync(() =>
        {
            StatusText = e.State switch
            {
                TerminalConnectionState.Connecting => "正在连接",
                TerminalConnectionState.Connected => "已连接",
                TerminalConnectionState.Faulted => $"连接异常: {e.Message}",
                _ => e.Message ?? "未连接",
            };
        });
    }

    private void OnSessionOutputReceived(object? sender, TerminalOutputEventArgs e)
    {
        lock (_outputSync)
        {
            _outputBuffer.Append(e.Text);
        }

        TryWriteLog(e.Text);
        OutputAppended?.Invoke(e.Text);
    }

    private void OpenLogWriter()
    {
        var safeName = string.Concat(Profile.Name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var logsDirectory = AppDataPaths.GetLogsDirectory();
        var logFilePath = Path.Combine(logsDirectory, $"{DateTime.Now:yyyyMMdd-HHmmss}-{safeName}.log");
        var writer = new StreamWriter(File.Open(logFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true,
        };

        lock (_logSync)
        {
            _logWriter = writer;
        }

        LogStatusText = $"日志: {Path.GetFileName(logFilePath)}";
    }

    private void UpdateConnectionState(TerminalConnectionState state)
    {
        if (_connectionState == state)
        {
            return;
        }

        _connectionState = state;
        OnPropertyChanged(nameof(IsConnected));
    }

    private static TerminalSessionStartRequest CloneRequest(TerminalSessionStartRequest request)
    {
        return new TerminalSessionStartRequest
        {
            Profile = CloneProfile(request.Profile),
            Secret = request.Secret,
            InitialColumns = request.InitialColumns,
            InitialRows = request.InitialRows,
            HostKeyVerificationCallback = request.HostKeyVerificationCallback,
        };
    }

    private static SessionProfile CloneProfile(SessionProfile profile)
    {
        return new SessionProfile
        {
            Id = profile.Id,
            Name = profile.Name,
            GroupPath = profile.GroupPath,
            Host = profile.Host,
            Port = profile.Port,
            Username = profile.Username,
            AuthType = profile.AuthType,
            PrivateKeyPath = profile.PrivateKeyPath,
            TerminalTheme = profile.TerminalTheme,
            LocalStartupDirectory = profile.LocalStartupDirectory,
            StartupDirectory = profile.StartupDirectory,
            KeepAliveSeconds = profile.KeepAliveSeconds,
            FontSize = profile.FontSize,
            TerminalFontFamily = profile.TerminalFontFamily,
            ScrollbackLines = profile.ScrollbackLines,
            IsFavorite = profile.IsFavorite,
            RememberSecret = profile.RememberSecret,
        };
    }

    private void TryWriteLog(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        try
        {
            lock (_logSync)
            {
                _logWriter?.Write(text);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ObjectDisposedException)
        {
            var writerToDispose = DetachLogWriter();
            writerToDispose?.Dispose();
            _ = _dispatcher.InvokeAsync(() =>
            {
                LogStatusText = $"日志: 写入失败 ({ex.GetType().Name})";
            });
        }
    }

    private StreamWriter? DetachLogWriter()
    {
        lock (_logSync)
        {
            var writer = _logWriter;
            _logWriter = null;
            return writer;
        }
    }
}
