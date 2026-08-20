using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;
using WiseShell.Core.Enums;
using WiseShell.Core.Interfaces;
using WiseShell.Core.Models;

namespace WiseShell.Transport.SshNet.Services;

public sealed class SshNetTerminalSession : ITerminalSession
{
    private readonly IHostKeyTrustStore _hostKeyTrustStore;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private SshClient? _client;
    private ShellStream? _shellStream;
    private TerminalSessionStartRequest? _activeRequest;
    private bool _disposed;

    public SshNetTerminalSession(IHostKeyTrustStore hostKeyTrustStore)
    {
        _hostKeyTrustStore = hostKeyTrustStore;
    }

    public event EventHandler<TerminalOutputEventArgs>? OutputReceived;

    public event EventHandler<TerminalConnectionStateChangedEventArgs>? ConnectionStateChanged;

    public async Task ConnectAsync(TerminalSessionStartRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);

        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            await CloseCoreAsync();
            _activeRequest = request;
            ChangeState(TerminalConnectionState.Connecting, "正在建立 SSH 连接...");

            try
            {
                var connectionInfo = SshConnectionInfoFactory.CreateConnectionInfo(request.Profile, request.Secret);
                var client = new SshClient(connectionInfo);

                if (request.Profile.KeepAliveSeconds > 0)
                {
                    client.KeepAliveInterval = TimeSpan.FromSeconds(request.Profile.KeepAliveSeconds);
                }

                client.HostKeyReceived += (_, args) =>
                {
                    args.CanTrust = HandleHostKeyVerification(request, args, cancellationToken);
                };

                await client.ConnectAsync(cancellationToken);

                var shell = client.CreateShellStream(
                    "xterm-256color",
                    (uint)Math.Max(40, request.InitialColumns),
                    (uint)Math.Max(12, request.InitialRows),
                    (uint)Math.Max(320, request.InitialColumns * 8),
                    (uint)Math.Max(192, request.InitialRows * 16),
                    4096);

                shell.DataReceived += OnShellDataReceived;
                shell.ErrorOccurred += OnShellErrorOccurred;
                shell.Closed += OnShellClosed;

                _client = client;
                _shellStream = shell;

                if (!string.IsNullOrWhiteSpace(request.Profile.StartupDirectory))
                {
                    shell.WriteLine($"cd {EscapeShellArgument(request.Profile.StartupDirectory)}");
                }

                ChangeState(TerminalConnectionState.Connected, "SSH 已连接。");
            }
            catch (Exception ex)
            {
                await CloseCoreAsync();
                ChangeState(TerminalConnectionState.Faulted, ex.Message);
                throw;
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task SendInputAsync(string input, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_shellStream is null)
        {
            return;
        }

        var buffer = Encoding.UTF8.GetBytes(input);
        await _shellStream.WriteAsync(buffer, 0, buffer.Length, cancellationToken);
        await _shellStream.FlushAsync(cancellationToken);
    }

    public Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        _shellStream?.ChangeWindowSize(
            (uint)Math.Max(columns, 40),
            (uint)Math.Max(rows, 12),
            (uint)Math.Max(columns * 8, 320),
            (uint)Math.Max(rows * 16, 192));

        return Task.CompletedTask;
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            await CloseCoreAsync();
            ChangeState(TerminalConnectionState.Disconnected, "连接已关闭。");
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _lifecycleLock.WaitAsync();
        try
        {
            await CloseCoreAsync();
        }
        finally
        {
            _lifecycleLock.Release();
            _lifecycleLock.Dispose();
        }
    }

    private bool HandleHostKeyVerification(TerminalSessionStartRequest request, HostKeyEventArgs args, CancellationToken cancellationToken)
    {
        var trustedHostKey = SshConnectionInfoFactory.ToTrustedHostKey(request.Profile, args);
        if (_hostKeyTrustStore.IsTrustedAsync(trustedHostKey, cancellationToken).GetAwaiter().GetResult())
        {
            return true;
        }

        if (request.HostKeyVerificationCallback is null)
        {
            return false;
        }

        var verificationRequest = SshConnectionInfoFactory.ToVerificationRequest(request.Profile, args);
        var accepted = request.HostKeyVerificationCallback(verificationRequest, cancellationToken).GetAwaiter().GetResult();
        if (accepted)
        {
            _hostKeyTrustStore.TrustAsync(trustedHostKey, cancellationToken).GetAwaiter().GetResult();
        }

        return accepted;
    }

    private Task CloseCoreAsync()
    {
        if (_shellStream is not null)
        {
            _shellStream.DataReceived -= OnShellDataReceived;
            _shellStream.ErrorOccurred -= OnShellErrorOccurred;
            _shellStream.Closed -= OnShellClosed;
            _shellStream.Dispose();
            _shellStream = null;
        }

        if (_client is not null)
        {
            try
            {
                if (_client.IsConnected)
                {
                    _client.Disconnect();
                }
            }
            finally
            {
                _client.Dispose();
                _client = null;
            }
        }

        return Task.CompletedTask;
    }

    private void OnShellDataReceived(object? sender, ShellDataEventArgs e)
    {
        if (e.Data is null || e.Data.Length == 0)
        {
            if (!string.IsNullOrEmpty(e.Line))
            {
                OutputReceived?.Invoke(this, new TerminalOutputEventArgs(e.Line));
            }

            return;
        }

        var text = Encoding.UTF8.GetString(e.Data);
        OutputReceived?.Invoke(this, new TerminalOutputEventArgs(text));
    }

    private void OnShellErrorOccurred(object? sender, ExceptionEventArgs e)
    {
        ChangeState(TerminalConnectionState.Faulted, e.Exception.Message);
    }

    private void OnShellClosed(object? sender, EventArgs e)
    {
        ChangeState(TerminalConnectionState.Disconnected, "远程终端已关闭。");
    }

    private void ChangeState(TerminalConnectionState state, string? message = null)
    {
        ConnectionStateChanged?.Invoke(this, new TerminalConnectionStateChangedEventArgs(state, message));
    }

    private static string EscapeShellArgument(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "''";
        }

        return $"'{value.Replace("'", "'\"'\"'")}'";
    }
}
