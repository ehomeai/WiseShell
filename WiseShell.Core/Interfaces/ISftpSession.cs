using WiseShell.Core.Models;

namespace WiseShell.Core.Interfaces;

public interface ISftpSession : IAsyncDisposable
{
    event EventHandler<SftpConnectionStateChangedEventArgs>? ConnectionStateChanged;

    event EventHandler<SftpTransferProgressChangedEventArgs>? TransferProgressChanged;

    bool IsConnected { get; }

    Task ConnectAsync(SftpConnectionRequest request, CancellationToken cancellationToken = default);

    Task<string> GetWorkingDirectoryAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SftpEntry>> ListDirectoryAsync(string path, CancellationToken cancellationToken = default);

    Task UploadAsync(string localPath, string remotePath, CancellationToken cancellationToken = default);

    Task DownloadAsync(string remotePath, string localPath, CancellationToken cancellationToken = default);

    Task DeleteAsync(string remotePath, bool isDirectory, CancellationToken cancellationToken = default);

    Task CreateDirectoryAsync(string remotePath, CancellationToken cancellationToken = default);

    Task RenameAsync(string remotePath, string newRemotePath, CancellationToken cancellationToken = default);
}
