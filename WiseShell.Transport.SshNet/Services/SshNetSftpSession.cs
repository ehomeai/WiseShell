using System.Net.Sockets;
using Renci.SshNet.Common;
using WiseShell.Core.Enums;
using WiseShell.Core.Interfaces;
using WiseShell.Core.Models;

namespace WiseShell.Transport.SshNet.Services;

public sealed class SshNetSftpSession : ISftpSession
{
    private readonly ISshNetSftpClientFactory _clientFactory;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private ISshNetSftpClient? _client;
    private SftpConnectionRequest? _activeRequest;
    private bool _hasConnectedOnce;
    private bool _disposed;

    public SshNetSftpSession(IHostKeyTrustStore hostKeyTrustStore)
        : this(new SshNetSftpClientFactory(hostKeyTrustStore))
    {
    }

    internal SshNetSftpSession(ISshNetSftpClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    public event EventHandler<SftpConnectionStateChangedEventArgs>? ConnectionStateChanged;

    public event EventHandler<SftpTransferProgressChangedEventArgs>? TransferProgressChanged;

    public bool IsConnected => _client?.IsConnected == true;

    public async Task ConnectAsync(SftpConnectionRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);

        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            await CloseCoreAsync();
            _activeRequest = request;
            _hasConnectedOnce = false;
        }
        finally
        {
            _lifecycleLock.Release();
        }

        await EnsureConnectedAsync(cancellationToken);
    }

    public Task<string> GetWorkingDirectoryAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteReadAsync(
            (client, innerCancellationToken) => client.GetWorkingDirectoryAsync(innerCancellationToken),
            cancellationToken);
    }

    public Task<IReadOnlyList<SftpEntry>> ListDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        return ExecuteReadAsync(
            (client, innerCancellationToken) => client.ListDirectoryAsync(path, innerCancellationToken),
            cancellationToken);
    }

    public Task UploadAsync(string localPath, string remotePath, CancellationToken cancellationToken = default)
    {
        return ExecuteWriteAsync(
            (client, innerCancellationToken) => client.UploadAsync(localPath, remotePath, OnTransferProgressChanged, innerCancellationToken),
            cancellationToken,
            "The connection was restored. Please retry the upload.");
    }

    public Task DownloadAsync(string remotePath, string localPath, CancellationToken cancellationToken = default)
    {
        return ExecuteReadAsync(
            async (client, innerCancellationToken) =>
            {
                await client.DownloadAsync(remotePath, localPath, OnTransferProgressChanged, innerCancellationToken);
                return 0;
            },
            cancellationToken);
    }

    public Task DeleteAsync(string remotePath, bool isDirectory, CancellationToken cancellationToken = default)
    {
        return ExecuteWriteAsync(
            (client, innerCancellationToken) => client.DeleteAsync(remotePath, isDirectory, innerCancellationToken),
            cancellationToken,
            "The connection was restored. Please retry the delete operation.");
    }

    public Task CreateDirectoryAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        return ExecuteWriteAsync(
            (client, innerCancellationToken) => client.CreateDirectoryAsync(remotePath, innerCancellationToken),
            cancellationToken,
            "The connection was restored. Please retry creating the directory.");
    }

    public Task RenameAsync(string remotePath, string newRemotePath, CancellationToken cancellationToken = default)
    {
        return ExecuteWriteAsync(
            (client, innerCancellationToken) => client.RenameAsync(remotePath, newRemotePath, innerCancellationToken),
            cancellationToken,
            "The connection was restored. Please retry the rename operation.");
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
            ChangeState(SftpConnectionState.Disconnected, "SFTP connection closed.");
        }
        finally
        {
            _lifecycleLock.Release();
            _lifecycleLock.Dispose();
        }
    }

    private async Task<T> ExecuteReadAsync<T>(
        Func<ISshNetSftpClient, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await EnsureConnectedAsync(cancellationToken);

        try
        {
            return await operation(GetRequiredClient(), cancellationToken);
        }
        catch (Exception ex) when (IsConnectionLostException(ex))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            await ReconnectAsync(cancellationToken);
            return await operation(GetRequiredClient(), cancellationToken);
        }
    }

    private async Task ExecuteWriteAsync(
        Func<ISshNetSftpClient, CancellationToken, Task> operation,
        CancellationToken cancellationToken,
        string recoveredMessage)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await EnsureConnectedAsync(cancellationToken);

        try
        {
            await operation(GetRequiredClient(), cancellationToken);
        }
        catch (Exception ex) when (IsConnectionLostException(ex))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            var recovered = await TryReconnectAsync(cancellationToken);
            throw new InvalidOperationException(
                recovered
                    ? recoveredMessage
                    : "The connection dropped and automatic reconnection failed.",
                ex);
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (IsConnected)
        {
            return;
        }

        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (IsConnected)
            {
                return;
            }

            var request = _activeRequest ?? throw new InvalidOperationException("No SFTP connection request is available.");
            await OpenClientAsync(request, cancellationToken, _hasConnectedOnce ? SftpConnectionState.Reconnecting : SftpConnectionState.Connecting);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private async Task ReconnectAsync(CancellationToken cancellationToken)
    {
        var recovered = await TryReconnectAsync(cancellationToken);
        if (!recovered)
        {
            throw new InvalidOperationException("Automatic reconnection failed.");
        }
    }

    private async Task<bool> TryReconnectAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return false;
        }

        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (IsConnected)
            {
                return true;
            }

            var request = _activeRequest;
            if (request is null)
            {
                return false;
            }

            try
            {
                await OpenClientAsync(request, cancellationToken, SftpConnectionState.Reconnecting);
                return true;
            }
            catch
            {
                return false;
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private async Task OpenClientAsync(
        SftpConnectionRequest request,
        CancellationToken cancellationToken,
        SftpConnectionState openingState)
    {
        var message = openingState == SftpConnectionState.Reconnecting
            ? "Reconnecting SFTP..."
            : "Connecting SFTP...";

        ChangeState(openingState, message);
        await CloseCoreAsync();

        try
        {
            var client = await _clientFactory.CreateAsync(request, cancellationToken);
            await client.ConnectAsync(cancellationToken);
            _client = client;
            _hasConnectedOnce = true;
            ChangeState(SftpConnectionState.Connected, "SFTP connected.");
        }
        catch (Exception ex)
        {
            await CloseCoreAsync();
            ChangeState(SftpConnectionState.Faulted, ex.Message);
            throw;
        }
    }

    private ISshNetSftpClient GetRequiredClient()
    {
        return _client ?? throw new InvalidOperationException("The SFTP session is not connected.");
    }

    private async Task CloseCoreAsync()
    {
        if (_client is null)
        {
            return;
        }

        try
        {
            _client.Disconnect();
        }
        finally
        {
            await _client.DisposeAsync();
            _client = null;
        }
    }

    private void ChangeState(SftpConnectionState state, string? message = null)
    {
        ConnectionStateChanged?.Invoke(this, new SftpConnectionStateChangedEventArgs(state, message));
    }

    private void OnTransferProgressChanged(SftpTransferProgressChangedEventArgs progress)
    {
        TransferProgressChanged?.Invoke(this, progress);
    }

    private static bool IsConnectionLostException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is OperationCanceledException)
            {
                return false;
            }

            if (current is SshConnectionException or SocketException or IOException or ObjectDisposedException)
            {
                return true;
            }
        }

        return false;
    }
}
