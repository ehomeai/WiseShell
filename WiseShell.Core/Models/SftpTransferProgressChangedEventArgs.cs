using WiseShell.Core.Enums;

namespace WiseShell.Core.Models;

public sealed class SftpTransferProgressChangedEventArgs : EventArgs
{
    public SftpTransferProgressChangedEventArgs(
        SftpTransferOperation operation,
        string localPath,
        string remotePath,
        long bytesTransferred,
        long totalBytes,
        bool isDirectoryTransfer,
        string? currentItemPath = null)
    {
        Operation = operation;
        LocalPath = localPath;
        RemotePath = remotePath;
        BytesTransferred = bytesTransferred;
        TotalBytes = totalBytes;
        IsDirectoryTransfer = isDirectoryTransfer;
        CurrentItemPath = currentItemPath;
    }

    public SftpTransferOperation Operation { get; }

    public string LocalPath { get; }

    public string RemotePath { get; }

    public long BytesTransferred { get; }

    public long TotalBytes { get; }

    public bool IsDirectoryTransfer { get; }

    public string? CurrentItemPath { get; }
}
