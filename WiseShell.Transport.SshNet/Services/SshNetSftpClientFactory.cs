using Renci.SshNet;
using Renci.SshNet.Common;
using WiseShell.Core.Enums;
using WiseShell.Core.Infrastructure;
using WiseShell.Core.Interfaces;
using WiseShell.Core.Models;

namespace WiseShell.Transport.SshNet.Services;

internal static class LocalUploadExclusions
{
    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".svn",
        ".git",
        ".hg",
        ".vs",
        ".vscode",
        ".idea",
        "bin",
        "obj",
        "artifacts",
        "node_modules",
        "packages",
        "TestResults",
        "EBWebView",
    };

    private static readonly HashSet<string> ExcludedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Thumbs.db",
        "Desktop.ini",
    };

    private static readonly string[] ExcludedFileSuffixes =
    [
        ".log",
        ".tmp",
        ".temp",
        ".user",
        ".suo",
        ".userosscache",
        ".sln.docstates",
    ];

    public static bool ShouldExcludeDirectory(string directoryPath)
    {
        return ExcludedDirectoryNames.Contains(Path.GetFileName(directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
    }

    public static bool ShouldExcludeFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        return ExcludedFileNames.Contains(fileName)
            || ExcludedFileSuffixes.Any(suffix => fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }
}

internal interface ISshNetSftpClientFactory
{
    Task<ISshNetSftpClient> CreateAsync(SftpConnectionRequest request, CancellationToken cancellationToken);
}

internal interface ISshNetSftpClient : IAsyncDisposable
{
    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken cancellationToken);

    Task<string> GetWorkingDirectoryAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SftpEntry>> ListDirectoryAsync(string path, CancellationToken cancellationToken);

    Task UploadAsync(string localPath, string remotePath, Action<SftpTransferProgressChangedEventArgs>? progressCallback, CancellationToken cancellationToken);

    Task DownloadAsync(string remotePath, string localPath, Action<SftpTransferProgressChangedEventArgs>? progressCallback, CancellationToken cancellationToken);

    Task DeleteAsync(string remotePath, bool isDirectory, CancellationToken cancellationToken);

    Task CreateDirectoryAsync(string remotePath, CancellationToken cancellationToken);

    Task RenameAsync(string remotePath, string newRemotePath, CancellationToken cancellationToken);

    void Disconnect();
}

internal sealed class SshNetSftpClientFactory : ISshNetSftpClientFactory
{
    private readonly IHostKeyTrustStore _hostKeyTrustStore;

    public SshNetSftpClientFactory(IHostKeyTrustStore hostKeyTrustStore)
    {
        _hostKeyTrustStore = hostKeyTrustStore;
    }

    public Task<ISshNetSftpClient> CreateAsync(SftpConnectionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var connectionInfo = SshConnectionInfoFactory.CreateConnectionInfo(request.Profile, request.Secret);
        var client = new SftpClient(connectionInfo);

        if (request.Profile.KeepAliveSeconds > 0)
        {
            client.KeepAliveInterval = TimeSpan.FromSeconds(request.Profile.KeepAliveSeconds);
        }

        client.HostKeyReceived += (_, args) =>
        {
            args.CanTrust = HandleHostKeyVerification(request, args, cancellationToken);
        };

        return Task.FromResult<ISshNetSftpClient>(new SshNetSftpClient(client));
    }

    private bool HandleHostKeyVerification(SftpConnectionRequest request, HostKeyEventArgs args, CancellationToken cancellationToken)
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
}

internal sealed class SshNetSftpClient : ISshNetSftpClient
{
    private readonly SftpClient _client;

    public SshNetSftpClient(SftpClient client)
    {
        _client = client;
    }

    public bool IsConnected => _client.IsConnected;

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        return _client.ConnectAsync(cancellationToken);
    }

    public Task<string> GetWorkingDirectoryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(NormalizePath(_client.WorkingDirectory));
    }

    public async Task<IReadOnlyList<SftpEntry>> ListDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        var result = new List<SftpEntry>();
        await foreach (var item in _client.ListDirectoryAsync(NormalizePath(path), cancellationToken))
        {
            if (item.Name is "." or "..")
            {
                continue;
            }

            result.Add(new SftpEntry
            {
                Name = item.Name,
                FullPath = item.FullName ?? item.Name,
                IsDirectory = item.IsDirectory,
                Size = item.Attributes.Size,
                LastWriteTimeUtc = new DateTimeOffset(item.Attributes.LastWriteTimeUtc),
                Permissions = item.Attributes.ToString() ?? string.Empty,
            });
        }

        return result
            .OrderByDescending(entry => entry.IsDirectory)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task UploadAsync(string localPath, string remotePath, Action<SftpTransferProgressChangedEventArgs>? progressCallback, CancellationToken cancellationToken)
    {
        if (Directory.Exists(localPath))
        {
            var totalBytes = CalculateLocalDirectorySize(localPath);
            await UploadDirectoryAsync(localPath, remotePath, totalBytes, progressCallback, cancellationToken);
            return;
        }

        var fileInfo = new FileInfo(localPath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("The local file does not exist.", localPath);
        }

        await using var localFile = File.OpenRead(localPath);
        await UploadFileCoreAsync(
            localPath,
            remotePath,
            localFile,
            fileInfo.Length,
            0L,
            fileInfo.Length,
            false,
            progressCallback,
            cancellationToken);
    }

    public async Task DownloadAsync(string remotePath, string localPath, Action<SftpTransferProgressChangedEventArgs>? progressCallback, CancellationToken cancellationToken)
    {
        var attributes = await _client.GetAttributesAsync(NormalizePath(remotePath), cancellationToken);
        if (attributes.IsDirectory)
        {
            var totalBytes = await CalculateRemoteDirectorySizeAsync(remotePath, cancellationToken);
            await DownloadDirectoryAsync(remotePath, localPath, totalBytes, progressCallback, cancellationToken);
            return;
        }

        await DownloadFileCoreAsync(
            remotePath,
            localPath,
            attributes.Size,
            0L,
            attributes.Size,
            false,
            progressCallback,
            cancellationToken);
    }

    public Task DeleteAsync(string remotePath, bool isDirectory, CancellationToken cancellationToken)
    {
        return isDirectory
            ? DeleteRemoteDirectoryRecursiveAsync(remotePath, cancellationToken)
            : _client.DeleteFileAsync(remotePath, cancellationToken);
    }

    public Task CreateDirectoryAsync(string remotePath, CancellationToken cancellationToken)
    {
        return _client.CreateDirectoryAsync(remotePath, cancellationToken);
    }

    public Task RenameAsync(string remotePath, string newRemotePath, CancellationToken cancellationToken)
    {
        return _client.RenameFileAsync(NormalizePath(remotePath), NormalizePath(newRemotePath), cancellationToken);
    }

    public void Disconnect()
    {
        if (_client.IsConnected)
        {
            _client.Disconnect();
        }
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ".";
        }

        return path;
    }

    private async Task<long> CalculateRemoteDirectorySizeAsync(string remotePath, CancellationToken cancellationToken)
    {
        long totalBytes = 0;
        await foreach (var item in _client.ListDirectoryAsync(NormalizePath(remotePath), cancellationToken))
        {
            if (item.Name is "." or "..")
            {
                continue;
            }

            if (item.IsDirectory)
            {
                totalBytes += await CalculateRemoteDirectorySizeAsync(item.FullName ?? item.Name, cancellationToken);
            }
            else
            {
                totalBytes += item.Attributes.Size;
            }
        }

        return totalBytes;
    }

    private async Task DownloadDirectoryAsync(
        string remotePath,
        string localPath,
        long totalBytes,
        Action<SftpTransferProgressChangedEventArgs>? progressCallback,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(localPath);
        var progressState = new DirectoryTransferState(totalBytes);

        if (totalBytes == 0)
        {
            progressCallback?.Invoke(
                new SftpTransferProgressChangedEventArgs(
                    SftpTransferOperation.Download,
                    localPath,
                    remotePath,
                    0,
                    0,
                    true,
                    remotePath));
        }

        await DownloadDirectoryCoreAsync(remotePath, localPath, progressState, progressCallback, cancellationToken);
    }

    private async Task DownloadDirectoryCoreAsync(
        string remotePath,
        string localPath,
        DirectoryTransferState progressState,
        Action<SftpTransferProgressChangedEventArgs>? progressCallback,
        CancellationToken cancellationToken)
    {
        await foreach (var item in _client.ListDirectoryAsync(NormalizePath(remotePath), cancellationToken))
        {
            if (item.Name is "." or "..")
            {
                continue;
            }

            var childRemotePath = item.FullName ?? item.Name;
            var childLocalPath = Path.Combine(localPath, item.Name);

            if (item.IsDirectory)
            {
                Directory.CreateDirectory(childLocalPath);
                await DownloadDirectoryCoreAsync(childRemotePath, childLocalPath, progressState, progressCallback, cancellationToken);
                continue;
            }

            var fileSize = item.Attributes.Size;
            await DownloadFileCoreAsync(
                childRemotePath,
                childLocalPath,
                fileSize,
                progressState.BytesTransferred,
                progressState.TotalBytes,
                true,
                progressCallback,
                cancellationToken);
            progressState.BytesTransferred += fileSize;
        }
    }

    private async Task DownloadFileCoreAsync(
        string remotePath,
        string localPath,
        long fileSize,
        long baseTransferredBytes,
        long totalBytesForTransfer,
        bool isDirectoryTransfer,
        Action<SftpTransferProgressChangedEventArgs>? progressCallback,
        CancellationToken cancellationToken)
    {
        var localDirectory = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrWhiteSpace(localDirectory))
        {
            Directory.CreateDirectory(localDirectory);
        }

        await using var localFile = File.Create(localPath);
        await RunWithTransferProgressAsync(
            () => _client.DownloadFile(
                remotePath,
                localFile,
                CreateProgressReporter(
                    totalBytesForTransfer > 0 ? totalBytesForTransfer : fileSize,
                    downloaded => progressCallback?.Invoke(
                        new SftpTransferProgressChangedEventArgs(
                            SftpTransferOperation.Download,
                            localPath,
                            remotePath,
                            baseTransferredBytes + downloaded,
                            totalBytesForTransfer > 0 ? totalBytesForTransfer : fileSize,
                            isDirectoryTransfer,
                            remotePath)))),
            cancellationToken);
    }

    private async Task UploadDirectoryAsync(
        string localPath,
        string remotePath,
        long totalBytes,
        Action<SftpTransferProgressChangedEventArgs>? progressCallback,
        CancellationToken cancellationToken)
    {
        await EnsureRemoteDirectoryExistsAsync(remotePath, cancellationToken);
        var progressState = new DirectoryTransferState(totalBytes);

        if (totalBytes == 0)
        {
            progressCallback?.Invoke(
                new SftpTransferProgressChangedEventArgs(
                    SftpTransferOperation.Upload,
                    localPath,
                    remotePath,
                    0,
                    0,
                    true,
                    remotePath));
        }

        await UploadDirectoryCoreAsync(localPath, remotePath, progressState, progressCallback, cancellationToken);
    }

    private async Task UploadDirectoryCoreAsync(
        string localPath,
        string remotePath,
        DirectoryTransferState progressState,
        Action<SftpTransferProgressChangedEventArgs>? progressCallback,
        CancellationToken cancellationToken)
    {
        await EnsureRemoteDirectoryExistsAsync(remotePath, cancellationToken);
        await DeleteRemoteExcludedEntriesAsync(remotePath, cancellationToken);

        foreach (var directoryPath in Directory
            .EnumerateDirectories(localPath)
            .Where(path => !LocalUploadExclusions.ShouldExcludeDirectory(path)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var childRemotePath = RemotePathHelper.Combine(remotePath, Path.GetFileName(directoryPath));
            await UploadDirectoryCoreAsync(directoryPath, childRemotePath, progressState, progressCallback, cancellationToken);
        }

        foreach (var filePath in Directory
            .EnumerateFiles(localPath)
            .Where(path => !LocalUploadExclusions.ShouldExcludeFile(path)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileInfo = new FileInfo(filePath);
            var childRemotePath = RemotePathHelper.Combine(remotePath, fileInfo.Name);

            await using var localFile = File.OpenRead(filePath);
            await UploadFileCoreAsync(
                filePath,
                childRemotePath,
                localFile,
                fileInfo.Length,
                progressState.BytesTransferred,
                progressState.TotalBytes,
                true,
                progressCallback,
                cancellationToken);
            progressState.BytesTransferred += fileInfo.Length;
        }
    }

    private async Task UploadFileCoreAsync(
        string localPath,
        string remotePath,
        Stream localFile,
        long fileSize,
        long baseTransferredBytes,
        long totalBytesForTransfer,
        bool isDirectoryTransfer,
        Action<SftpTransferProgressChangedEventArgs>? progressCallback,
        CancellationToken cancellationToken)
    {
        await RunWithTransferProgressAsync(
            () => _client.UploadFile(
                localFile,
                remotePath,
                true,
                CreateProgressReporter(
                    totalBytesForTransfer > 0 ? totalBytesForTransfer : fileSize,
                    uploaded => progressCallback?.Invoke(
                        new SftpTransferProgressChangedEventArgs(
                            SftpTransferOperation.Upload,
                            localPath,
                            remotePath,
                            baseTransferredBytes + uploaded,
                            totalBytesForTransfer > 0 ? totalBytesForTransfer : fileSize,
                            isDirectoryTransfer,
                            remotePath)))),
            cancellationToken);
    }

    private async Task EnsureRemoteDirectoryExistsAsync(string remotePath, CancellationToken cancellationToken)
    {
        var normalizedPath = NormalizePath(remotePath);
        try
        {
            var attributes = await _client.GetAttributesAsync(normalizedPath, cancellationToken);
            if (!attributes.IsDirectory)
            {
                throw new IOException($"Remote path exists and is not a directory: {normalizedPath}");
            }

            return;
        }
        catch (SftpPathNotFoundException)
        {
        }

        await _client.CreateDirectoryAsync(normalizedPath, cancellationToken);
    }

    private async Task DeleteRemoteExcludedEntriesAsync(string remotePath, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in _client.ListDirectoryAsync(NormalizePath(remotePath), cancellationToken))
            {
                if (item.Name is "." or "..")
                {
                    continue;
                }

                var childRemotePath = item.FullName ?? RemotePathHelper.Combine(remotePath, item.Name);
                if (item.IsDirectory && LocalUploadExclusions.ShouldExcludeDirectory(item.Name))
                {
                    await DeleteRemoteDirectoryRecursiveAsync(childRemotePath, cancellationToken);
                    continue;
                }

                if (!item.IsDirectory && LocalUploadExclusions.ShouldExcludeFile(item.Name))
                {
                    await _client.DeleteFileAsync(childRemotePath, cancellationToken);
                }
            }
        }
        catch (SftpPathNotFoundException)
        {
        }
    }

    private async Task DeleteRemoteDirectoryRecursiveAsync(string remotePath, CancellationToken cancellationToken)
    {
        var normalizedPath = NormalizePath(remotePath);
        await foreach (var item in _client.ListDirectoryAsync(normalizedPath, cancellationToken))
        {
            if (item.Name is "." or "..")
            {
                continue;
            }

            var childRemotePath = item.FullName ?? RemotePathHelper.Combine(normalizedPath, item.Name);
            if (item.IsDirectory)
            {
                await DeleteRemoteDirectoryRecursiveAsync(childRemotePath, cancellationToken);
                continue;
            }

            await _client.DeleteFileAsync(childRemotePath, cancellationToken);
        }

        await _client.DeleteDirectoryAsync(normalizedPath, cancellationToken);
    }

    private static long CalculateLocalDirectorySize(string localPath)
    {
        long totalBytes = 0;
        foreach (var filePath in EnumerateUploadFiles(localPath))
        {
            totalBytes += new FileInfo(filePath).Length;
        }

        return totalBytes;
    }

    private static IEnumerable<string> EnumerateUploadFiles(string localPath)
    {
        foreach (var directoryPath in Directory
            .EnumerateDirectories(localPath)
            .Where(path => !LocalUploadExclusions.ShouldExcludeDirectory(path)))
        {
            foreach (var filePath in EnumerateUploadFiles(directoryPath))
            {
                yield return filePath;
            }
        }

        foreach (var filePath in Directory
            .EnumerateFiles(localPath)
            .Where(path => !LocalUploadExclusions.ShouldExcludeFile(path)))
        {
            yield return filePath;
        }
    }

    private async Task RunWithTransferProgressAsync(Action transferAction, CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(
            static clientState => ((SftpClient)clientState!).Disconnect(),
            _client);

        var canceledByDisconnect = false;
        await Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    transferAction();
                }
                catch (Exception ex) when (cancellationToken.IsCancellationRequested && IsCanceledByDisconnect(ex))
                {
                    canceledByDisconnect = true;
                }
            },
            cancellationToken);

        if (canceledByDisconnect)
        {
            throw new OperationCanceledException("The SFTP transfer was canceled.", cancellationToken);
        }
    }

    private static bool IsCanceledByDisconnect(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is InvalidOperationException or SshConnectionException or ObjectDisposedException)
            {
                var message = current.Message ?? string.Empty;
                if (message.Contains("session is not open", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("not connected", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("client not connected", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static Action<ulong> CreateProgressReporter(long totalBytes, Action<long> reportProgress)
    {
        long lastReportedBytes = -1;
        var reportStep = totalBytes > 0
            ? Math.Max(256 * 1024L, totalBytes / 100L)
            : 256 * 1024L;

        return transferred =>
        {
            var currentBytes = (long)transferred;
            if (currentBytes < totalBytes && lastReportedBytes >= 0 && currentBytes - lastReportedBytes < reportStep)
            {
                return;
            }

            lastReportedBytes = currentBytes;
            reportProgress(currentBytes);
        };
    }

    private sealed class DirectoryTransferState
    {
        public DirectoryTransferState(long totalBytes)
        {
            TotalBytes = totalBytes;
        }

        public long TotalBytes { get; }

        public long BytesTransferred { get; set; }
    }
}
