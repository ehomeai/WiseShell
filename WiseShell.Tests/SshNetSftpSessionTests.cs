using WiseShell.Core.Models;
using WiseShell.Transport.SshNet.Services;

namespace WiseShell.Tests;

public sealed class SshNetSftpSessionTests
{
    [Fact]
    public async Task MultipleReads_ReuseSingleConnectedClient()
    {
        var client = new FakeSftpClient();
        client.WorkingDirectory = "/home/tester";
        client.ListDirectoryHandler = _ => Task.FromResult<IReadOnlyList<SftpEntry>>(
        [
            new SftpEntry { Name = "file.txt", FullPath = "/file.txt" },
        ]);
        var factory = new FakeSftpClientFactory(client);
        await using var session = new SshNetSftpSession(factory);

        await session.ConnectAsync(CreateRequest());
        await session.ListDirectoryAsync(".");
        await session.ListDirectoryAsync(".");

        Assert.Equal(1, factory.CreateCallCount);
        Assert.Equal(1, client.ConnectCallCount);
        Assert.Equal(2, client.ListDirectoryCallCount);
    }

    [Fact]
    public async Task GetWorkingDirectory_ReturnsConnectedClientDirectory()
    {
        var client = new FakeSftpClient
        {
            WorkingDirectory = "/srv/data",
        };
        var factory = new FakeSftpClientFactory(client);
        await using var session = new SshNetSftpSession(factory);

        await session.ConnectAsync(CreateRequest());
        var workingDirectory = await session.GetWorkingDirectoryAsync();

        Assert.Equal("/srv/data", workingDirectory);
        Assert.Equal(1, client.GetWorkingDirectoryCallCount);
    }

    [Fact]
    public async Task ReadOperation_ReconnectsAndRetriesAfterConnectionDrop()
    {
        var firstClient = new FakeSftpClient();
        firstClient.WorkingDirectory = "/first";
        firstClient.ListDirectoryHandler = _ => throw firstClient.DisconnectAndCreateException();

        var secondClient = new FakeSftpClient();
        secondClient.WorkingDirectory = "/second";
        secondClient.ListDirectoryHandler = _ => Task.FromResult<IReadOnlyList<SftpEntry>>(
        [
            new SftpEntry { Name = "retry.txt", FullPath = "/retry.txt" },
        ]);
        var factory = new FakeSftpClientFactory(firstClient, secondClient);
        await using var session = new SshNetSftpSession(factory);

        await session.ConnectAsync(CreateRequest());
        var items = await session.ListDirectoryAsync(".");

        Assert.Single(items);
        Assert.Equal("retry.txt", items[0].Name);
        Assert.Equal(2, factory.CreateCallCount);
        Assert.Equal(1, firstClient.ListDirectoryCallCount);
        Assert.Equal(1, secondClient.ListDirectoryCallCount);
    }

    [Fact]
    public async Task WriteOperation_DoesNotRetryAfterConnectionDrop()
    {
        var firstClient = new FakeSftpClient();
        firstClient.WorkingDirectory = "/first";
        firstClient.UploadHandler = (_, _, _) => throw firstClient.DisconnectAndCreateException();
        var secondClient = new FakeSftpClient
        {
            WorkingDirectory = "/second",
        };
        var factory = new FakeSftpClientFactory(firstClient, secondClient);
        await using var session = new SshNetSftpSession(factory);

        await session.ConnectAsync(CreateRequest());
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => session.UploadAsync("local.txt", "/remote.txt"));

        Assert.Contains("retry", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, factory.CreateCallCount);
        Assert.Equal(1, firstClient.UploadCallCount);
        Assert.Equal(0, secondClient.UploadCallCount);
    }

    [Fact]
    public async Task UploadProgress_IsForwardedToSessionObservers()
    {
        var client = new FakeSftpClient
        {
            WorkingDirectory = "/upload",
            UploadHandler = (_, _, progress) =>
            {
                progress?.Invoke(new SftpTransferProgressChangedEventArgs(
                    Core.Enums.SftpTransferOperation.Upload,
                    @"C:\local.txt",
                    "/remote.txt",
                    64,
                    128,
                    false));
                return Task.CompletedTask;
            },
        };
        var factory = new FakeSftpClientFactory(client);
        await using var session = new SshNetSftpSession(factory);
        SftpTransferProgressChangedEventArgs? observedProgress = null;
        session.TransferProgressChanged += (_, args) => observedProgress = args;

        await session.ConnectAsync(CreateRequest());
        await session.UploadAsync(@"C:\local.txt", "/remote.txt");

        Assert.NotNull(observedProgress);
        Assert.Equal(64, observedProgress!.BytesTransferred);
        Assert.Equal(128, observedProgress.TotalBytes);
    }

    [Fact]
    public async Task RenameOperation_UsesConnectedClient()
    {
        var client = new FakeSftpClient
        {
            WorkingDirectory = "/rename",
        };
        var factory = new FakeSftpClientFactory(client);
        await using var session = new SshNetSftpSession(factory);

        await session.ConnectAsync(CreateRequest());
        await session.RenameAsync("/old-name.txt", "/new-name.txt");

        Assert.Equal(1, client.RenameCallCount);
        Assert.Equal("/old-name.txt", client.LastRenameSourcePath);
        Assert.Equal("/new-name.txt", client.LastRenameTargetPath);
    }

    private static SftpConnectionRequest CreateRequest()
    {
        return new SftpConnectionRequest
        {
            Profile = new SessionProfile
            {
                Name = "test",
                Host = "127.0.0.1",
                Username = "tester",
            },
            Secret = "secret",
        };
    }

    private sealed class FakeSftpClientFactory : ISshNetSftpClientFactory
    {
        private readonly Queue<FakeSftpClient> _clients;

        public FakeSftpClientFactory(params FakeSftpClient[] clients)
        {
            _clients = new Queue<FakeSftpClient>(clients);
        }

        public int CreateCallCount { get; private set; }

        public Task<ISshNetSftpClient> CreateAsync(SftpConnectionRequest request, CancellationToken cancellationToken)
        {
            CreateCallCount++;
            if (_clients.Count == 0)
            {
                throw new InvalidOperationException("No fake clients are available.");
            }

            return Task.FromResult<ISshNetSftpClient>(_clients.Dequeue());
        }
    }

    private sealed class FakeSftpClient : ISshNetSftpClient
    {
        public string WorkingDirectory { get; set; } = "/";

        public Func<string, Task<IReadOnlyList<SftpEntry>>>? ListDirectoryHandler { get; set; }

        public Func<string, string, Action<SftpTransferProgressChangedEventArgs>?, Task>? UploadHandler { get; set; }

        public Func<string, string, Action<SftpTransferProgressChangedEventArgs>?, Task>? DownloadHandler { get; set; }

        public Func<string, bool, Task>? DeleteHandler { get; set; }

        public Func<string, Task>? CreateDirectoryHandler { get; set; }

        public Func<string, string, Task>? RenameHandler { get; set; }

        public int ConnectCallCount { get; private set; }

        public int ListDirectoryCallCount { get; private set; }

        public int GetWorkingDirectoryCallCount { get; private set; }

        public int UploadCallCount { get; private set; }

        public int DownloadCallCount { get; private set; }

        public int DeleteCallCount { get; private set; }

        public int CreateDirectoryCallCount { get; private set; }

        public int RenameCallCount { get; private set; }

        public string? LastRenameSourcePath { get; private set; }

        public string? LastRenameTargetPath { get; private set; }

        public bool IsConnected { get; private set; }

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            ConnectCallCount++;
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task<string> GetWorkingDirectoryAsync(CancellationToken cancellationToken)
        {
            GetWorkingDirectoryCallCount++;
            return Task.FromResult(WorkingDirectory);
        }

        public Task<IReadOnlyList<SftpEntry>> ListDirectoryAsync(string path, CancellationToken cancellationToken)
        {
            ListDirectoryCallCount++;
            return ListDirectoryHandler?.Invoke(path) ?? Task.FromResult<IReadOnlyList<SftpEntry>>(Array.Empty<SftpEntry>());
        }

        public Task UploadAsync(string localPath, string remotePath, Action<SftpTransferProgressChangedEventArgs>? progressCallback, CancellationToken cancellationToken)
        {
            UploadCallCount++;
            return UploadHandler?.Invoke(localPath, remotePath, progressCallback) ?? Task.CompletedTask;
        }

        public Task DownloadAsync(string remotePath, string localPath, Action<SftpTransferProgressChangedEventArgs>? progressCallback, CancellationToken cancellationToken)
        {
            DownloadCallCount++;
            return DownloadHandler?.Invoke(remotePath, localPath, progressCallback) ?? Task.CompletedTask;
        }

        public Task DeleteAsync(string remotePath, bool isDirectory, CancellationToken cancellationToken)
        {
            DeleteCallCount++;
            return DeleteHandler?.Invoke(remotePath, isDirectory) ?? Task.CompletedTask;
        }

        public Task CreateDirectoryAsync(string remotePath, CancellationToken cancellationToken)
        {
            CreateDirectoryCallCount++;
            return CreateDirectoryHandler?.Invoke(remotePath) ?? Task.CompletedTask;
        }

        public Task RenameAsync(string remotePath, string newRemotePath, CancellationToken cancellationToken)
        {
            RenameCallCount++;
            LastRenameSourcePath = remotePath;
            LastRenameTargetPath = newRemotePath;
            return RenameHandler?.Invoke(remotePath, newRemotePath) ?? Task.CompletedTask;
        }

        public void Disconnect()
        {
            IsConnected = false;
        }

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            return ValueTask.CompletedTask;
        }

        public IOException DisconnectAndCreateException()
        {
            IsConnected = false;
            return new IOException("connection dropped");
        }
    }
}
