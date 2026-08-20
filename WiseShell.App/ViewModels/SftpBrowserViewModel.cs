using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using WiseShell.App.Infrastructure;
using WiseShell.App.Services;
using WiseShell.Core.Enums;
using WiseShell.Core.Infrastructure;
using WiseShell.Core.Interfaces;
using WiseShell.Core.Models;

namespace WiseShell.App.ViewModels;

public sealed class SftpBrowserViewModel : ObservableObject, IAsyncDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly IDialogService _dialogService;
    private readonly ISessionSecretCache _sessionSecretCache;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly object _transferQueueSync = new();
    private readonly List<TransferQueueItem> _pendingTransfers = [];
    private readonly List<TransferLogEntry> _selectedTransferLogs = [];
    private readonly SftpConnectionRequest _request;
    private readonly ISftpSession _session;

    private string _currentPath = "/";
    private string _currentLocalPath = GetDefaultLocalPath();
    private string _remotePathInput = "/";
    private string _localPathInput = GetDefaultLocalPath();
    private string? _selectedLocalDrive;
    private string _statusText = "就绪";
    private string _overlayTitle = "正在连接 SFTP...";
    private string _overlayMessage = "正在建立连接并加载远程目录，请稍候。";
    private string _localSummaryText = "0 个文件夹，0 个文件";
    private string _remoteSummaryText = "0 个文件，0 个目录，大小总计: 0 B";
    private string _transferLogSummaryText = "暂无传输记录";
    private Visibility _overlayVisibility = Visibility.Visible;
    private Visibility _retryVisibility = Visibility.Collapsed;
    private SftpEntry? _selectedEntry;
    private LocalFileEntry? _selectedLocalEntry;
    private TransferLogEntry? _activeTransferLogEntry;
    private CancellationTokenSource? _activeTransferCts;
    private bool _isLocalBusy;
    private bool _isRemoteBusy;
    private bool _isTransferRunning;
    private bool _hasPendingTransfers;
    private bool _isTransferProcessorRunning;
    private bool _isReady;
    private bool _isInitializing;
    private bool _secretCached;
    private bool _disposed;

    public SftpBrowserViewModel(
        Dispatcher dispatcher,
        SftpConnectionRequest request,
        ISftpSessionFactory sftpSessionFactory,
        ISessionSecretCache sessionSecretCache,
        IDialogService dialogService)
    {
        ArgumentNullException.ThrowIfNull(sftpSessionFactory);
        ArgumentNullException.ThrowIfNull(request);

        _dispatcher = dispatcher;
        _dialogService = dialogService;
        _sessionSecretCache = sessionSecretCache;
        Profile = CloneProfile(request.Profile);
        _request = new SftpConnectionRequest
        {
            Profile = Profile,
            Secret = request.Secret,
            HostKeyVerificationCallback = request.HostKeyVerificationCallback,
        };

        _session = sftpSessionFactory.CreateSession();
        _session.ConnectionStateChanged += OnSessionConnectionStateChanged;
        _session.TransferProgressChanged += OnSessionTransferProgressChanged;

        _currentLocalPath = ResolveInitialLocalPath(Profile.LocalStartupDirectory);
        RefreshLocalDrives();
        _localPathInput = _currentLocalPath;
        _remotePathInput = _currentPath;
    }

    public SessionProfile Profile { get; }

    public ObservableCollection<LocalFileEntry> LocalEntries { get; } = new();

    public ObservableCollection<SftpEntry> Entries { get; } = new();

    public ObservableCollection<TransferLogEntry> TransferLogs { get; } = new();

    public ObservableCollection<string> LocalDrives { get; } = new();

    public string WindowTitle => $"SFTP - {Profile.Name}";

    public string CurrentPath
    {
        get => _currentPath;
        private set
        {
            if (SetProperty(ref _currentPath, value))
            {
                RemotePathInput = value;
                OnPropertyChanged(nameof(CanNavigateRemoteUp));
            }
        }
    }

    public string CurrentLocalPath
    {
        get => _currentLocalPath;
        private set
        {
            if (SetProperty(ref _currentLocalPath, value))
            {
                LocalPathInput = value;
                RefreshLocalDrives();
                OnPropertyChanged(nameof(CanNavigateLocalUp));
                OnPropertyChanged(nameof(CanDownloadSelection));
            }
        }
    }

    public string RemotePathInput
    {
        get => _remotePathInput;
        set => SetProperty(ref _remotePathInput, value);
    }

    public string LocalPathInput
    {
        get => _localPathInput;
        set => SetProperty(ref _localPathInput, value);
    }

    public string? SelectedLocalDrive
    {
        get => _selectedLocalDrive;
        private set => SetProperty(ref _selectedLocalDrive, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string OverlayTitle
    {
        get => _overlayTitle;
        private set => SetProperty(ref _overlayTitle, value);
    }

    public string OverlayMessage
    {
        get => _overlayMessage;
        private set => SetProperty(ref _overlayMessage, value);
    }

    public string LocalSummaryText
    {
        get => _localSummaryText;
        private set => SetProperty(ref _localSummaryText, value);
    }

    public string RemoteSummaryText
    {
        get => _remoteSummaryText;
        private set => SetProperty(ref _remoteSummaryText, value);
    }

    public string TransferLogSummaryText
    {
        get => _transferLogSummaryText;
        private set => SetProperty(ref _transferLogSummaryText, value);
    }

    public Visibility OverlayVisibility
    {
        get => _overlayVisibility;
        private set => SetProperty(ref _overlayVisibility, value);
    }

    public Visibility RetryVisibility
    {
        get => _retryVisibility;
        private set => SetProperty(ref _retryVisibility, value);
    }

    public bool IsLocalBusy
    {
        get => _isLocalBusy;
        private set
        {
            if (SetProperty(ref _isLocalBusy, value))
            {
                NotifyOperationStateChanged();
            }
        }
    }

    public bool IsRemoteBusy
    {
        get => _isRemoteBusy;
        private set
        {
            if (SetProperty(ref _isRemoteBusy, value))
            {
                NotifyOperationStateChanged();
            }
        }
    }

    public bool IsTransferRunning
    {
        get => _isTransferRunning;
        private set
        {
            if (SetProperty(ref _isTransferRunning, value))
            {
                NotifyOperationStateChanged();
                UpdateTransferLogSummary();
            }
        }
    }

    public bool HasPendingTransfers
    {
        get => _hasPendingTransfers;
        private set
        {
            if (SetProperty(ref _hasPendingTransfers, value))
            {
                NotifyOperationStateChanged();
                UpdateTransferLogSummary();
            }
        }
    }

    public bool IsReady
    {
        get => _isReady;
        private set
        {
            if (SetProperty(ref _isReady, value))
            {
                NotifyOperationStateChanged();
            }
        }
    }

    public bool IsInitializing
    {
        get => _isInitializing;
        private set
        {
            if (SetProperty(ref _isInitializing, value))
            {
                NotifyOperationStateChanged();
            }
        }
    }

    public bool CanInteractLocal => !_disposed && !IsLocalBusy && !IsInitializing;

    public bool CanInteractRemote => !_disposed && IsReady && !IsRemoteBusy && !IsInitializing && !IsTransferRunning && !HasPendingTransfers;

    public bool CanSelectRemoteEntries => !_disposed && IsReady && !IsRemoteBusy && !IsInitializing;

    public bool CanQueueTransfers => !_disposed && IsReady && !IsRemoteBusy && !IsInitializing;

    public bool CanRetry => !_disposed && !IsRemoteBusy && !IsInitializing;

    public bool CanNavigateRemoteUp => CanInteractRemote && CurrentPath != "/";

    public bool CanNavigateLocalUp => CanInteractLocal && HasLocalParent(CurrentLocalPath);

    public bool CanUploadSelection => CanQueueTransfers && SelectedLocalEntry is not null && !SelectedLocalEntry.IsParentNavigation;

    public bool CanDownloadSelection => CanQueueTransfers && SelectedEntry is not null && !SelectedEntry.IsParentNavigation && Directory.Exists(CurrentLocalPath);

    public bool CanDeleteLocalSelection => CanInteractLocal && SelectedLocalEntry is not null && !SelectedLocalEntry.IsParentNavigation;

    public bool CanDeleteSelection => CanInteractRemote && SelectedEntry is not null && !SelectedEntry.IsParentNavigation;

    public bool CanRenameLocalSelection => CanInteractLocal && SelectedLocalEntry is not null && !SelectedLocalEntry.IsParentNavigation;

    public bool CanRenameSelection => CanInteractRemote && SelectedEntry is not null && !SelectedEntry.IsParentNavigation;

    public bool CanCancelSelectedTransfers => _selectedTransferLogs.Any(entry => entry.IsCancelable);

    public bool CanCancelAllTransfers => !_disposed && (IsTransferRunning || HasPendingTransfers);

    public bool CanClearTransferLogs => TransferLogs.Any(entry => !entry.IsCancelable);

    public SftpEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetProperty(ref _selectedEntry, value))
            {
                OnPropertyChanged(nameof(CanDownloadSelection));
                OnPropertyChanged(nameof(CanDeleteSelection));
                OnPropertyChanged(nameof(CanRenameSelection));
            }
        }
    }

    public LocalFileEntry? SelectedLocalEntry
    {
        get => _selectedLocalEntry;
        set
        {
            if (SetProperty(ref _selectedLocalEntry, value))
            {
                OnPropertyChanged(nameof(CanUploadSelection));
                OnPropertyChanged(nameof(CanDeleteLocalSelection));
                OnPropertyChanged(nameof(CanRenameLocalSelection));
            }
        }
    }

    public void SetSelectedTransferLogs(IEnumerable<TransferLogEntry> entries)
    {
        _selectedTransferLogs.Clear();
        _selectedTransferLogs.AddRange(entries.Where(entry => entry is not null));
        OnPropertyChanged(nameof(CanCancelSelectedTransfers));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await LoadLocalEntriesAsync(CurrentLocalPath, cancellationToken);
        await ConnectAndLoadAsync(isRetry: false, cancellationToken);
    }

    public Task RetryAsync(CancellationToken cancellationToken = default)
    {
        return ConnectAndLoadAsync(isRetry: true, cancellationToken);
    }

    public Task NavigateRemoteToInputAsync(CancellationToken cancellationToken = default)
    {
        var targetPath = ResolveRemoteInputPath(RemotePathInput);
        return ExecuteRemoteBusyAsync(
            async innerCancellationToken =>
            {
                var itemCount = await LoadRemoteEntriesCoreAsync(targetPath, innerCancellationToken);
                StatusText = $"已进入远程目录 {targetPath}，共 {itemCount} 项。";
            },
            cancellationToken);
    }

    public Task NavigateLocalToInputAsync(CancellationToken cancellationToken = default)
    {
        var targetPath = ResolveLocalInputPath(LocalPathInput);
        return LoadLocalEntriesAsync(targetPath, cancellationToken, $"宸茶繘鍏ユ湰鍦扮洰褰? {targetPath}");
    }

    public Task NavigateToLocalDriveAsync(string? driveRoot, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(driveRoot))
        {
            return Task.CompletedTask;
        }

        var normalizedDriveRoot = NormalizeLocalDriveRoot(driveRoot);
        if (string.IsNullOrWhiteSpace(normalizedDriveRoot))
        {
            return Task.CompletedTask;
        }

        var currentDriveRoot = GetLocalDriveRoot(CurrentLocalPath);
        if (string.Equals(currentDriveRoot, normalizedDriveRoot, StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        return LoadLocalEntriesAsync(normalizedDriveRoot, cancellationToken, $"已切换到本地磁盘: {normalizedDriveRoot}");
    }

    public Task RefreshRemoteAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteRemoteBusyAsync(
            async innerCancellationToken =>
            {
                var itemCount = await LoadRemoteEntriesCoreAsync(CurrentPath, innerCancellationToken);
                StatusText = $"已刷新远程目录，共 {itemCount} 项。";
            },
            cancellationToken);
    }

    public Task RefreshLocalAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteLocalBusyAsync(
            async innerCancellationToken =>
            {
                var itemCount = await LoadLocalEntriesCoreAsync(CurrentLocalPath, innerCancellationToken);
                StatusText = $"已刷新本地目录，共 {itemCount} 项。";
            },
            cancellationToken);
    }

    public Task NavigateToAsync(SftpEntry entry, CancellationToken cancellationToken = default)
    {
        if (!entry.IsDirectory)
        {
            return Task.CompletedTask;
        }

        var targetPath = entry.IsParentNavigation
            ? entry.FullPath
            : RemotePathHelper.Resolve(CurrentPath, entry.FullPath);

        return ExecuteRemoteBusyAsync(
            async innerCancellationToken =>
            {
                var itemCount = await LoadRemoteEntriesCoreAsync(targetPath, innerCancellationToken);
                StatusText = $"已进入远程目录 {targetPath}，共 {itemCount} 项。";
            },
            cancellationToken);
    }

    public Task NavigateLocalToAsync(LocalFileEntry entry, CancellationToken cancellationToken = default)
    {
        if (!entry.IsDirectory)
        {
            return Task.CompletedTask;
        }

        return LoadLocalEntriesAsync(entry.FullPath, cancellationToken, $"宸茶繘鍏ユ湰鍦扮洰褰? {entry.FullPath}");
    }

    public Task NavigateRemoteUpAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentPath == "/")
        {
            return Task.CompletedTask;
        }

        var parentPath = RemotePathHelper.GetParent(CurrentPath);
        return ExecuteRemoteBusyAsync(
            async innerCancellationToken =>
            {
                var itemCount = await LoadRemoteEntriesCoreAsync(parentPath, innerCancellationToken);
                StatusText = $"已进入远程目录 {parentPath}，共 {itemCount} 项。";
            },
            cancellationToken);
    }

    public Task NavigateLocalUpAsync(CancellationToken cancellationToken = default)
    {
        if (!HasLocalParent(CurrentLocalPath))
        {
            return Task.CompletedTask;
        }

        var parentPath = GetLocalParent(CurrentLocalPath);
        return LoadLocalEntriesAsync(parentPath, cancellationToken, $"宸茶繘鍏ユ湰鍦扮洰褰? {parentPath}");
    }

    public async Task UploadSelectedLocalAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedLocalEntry is null)
        {
            await _dialogService.ShowInfoAsync("上传到远程", "请先在左侧选择一个本地文件。", cancellationToken);
            return;
        }

        await UploadAsync(SelectedLocalEntry.FullPath, cancellationToken);
    }

    public Task UploadAsync(string localPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(localPath))
        {
            return Task.CompletedTask;
        }

        var isDirectory = Directory.Exists(localPath);
        if (!isDirectory && !File.Exists(localPath))
        {
            return Task.CompletedTask;
        }

        var fileName = Path.GetFileName(localPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var remotePath = RemotePathHelper.Combine(CurrentPath, fileName);
        var operationName = isDirectory ? "目录" : "文件";
        var logEntry = StartTransferLog("上传", localPath, remotePath, $"等待上传{operationName} {fileName}...");
        EnqueueTransfer(new TransferQueueItem(
            TransferKind.Upload,
            localPath,
            remotePath,
            isDirectory,
            fileName,
            logEntry));

        StatusText = $"已加入上传队列: {fileName}";
        return Task.CompletedTask;
    }

    public async Task DownloadSelectedRemoteAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedEntry is null)
        {
            await _dialogService.ShowInfoAsync("下载到本地", "请先在右侧选择一个远程文件或目录。", cancellationToken);
            return;
        }

        var localPath = Path.Combine(CurrentLocalPath, SelectedEntry.Name);
        await DownloadAsync(SelectedEntry, localPath, cancellationToken);
    }

    public async Task DownloadAsync(SftpEntry? entry, string localPath, CancellationToken cancellationToken = default)
    {
        if (entry is null || entry.IsParentNavigation)
        {
            return;
        }

        if (!entry.IsDirectory && !await EnsureLocalFileTargetReadyAsync(localPath, cancellationToken))
        {
            return;
        }

        if (entry.IsDirectory && !Directory.Exists(localPath))
        {
            Directory.CreateDirectory(localPath);
        }

        var operationName = entry.IsDirectory ? "目录" : "文件";
        var logEntry = StartTransferLog("下载", localPath, entry.FullPath, $"等待下载{operationName} {entry.Name}...");
        EnqueueTransfer(new TransferQueueItem(
            TransferKind.Download,
            localPath,
            entry.FullPath,
            entry.IsDirectory,
            entry.Name,
            logEntry));

        StatusText = $"已加入下载队列: {entry.Name}";
    }

    public async Task DeleteSelectedLocalAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedLocalEntry is null || SelectedLocalEntry.IsParentNavigation)
        {
            return;
        }

        var target = SelectedLocalEntry;
        var confirmed = await _dialogService.ConfirmDeleteAsync("删除本地项", $"确定要删除“{target.Name}”吗？", cancellationToken);
        if (!confirmed)
        {
            return;
        }

        await ExecuteLocalBusyAsync(
            async innerCancellationToken =>
            {
                if (target.IsDirectory)
                {
                    Directory.Delete(target.FullPath, recursive: true);
                }
                else if (File.Exists(target.FullPath))
                {
                    File.Delete(target.FullPath);
                }

                var itemCount = await LoadLocalEntriesCoreAsync(CurrentLocalPath, innerCancellationToken);
                StatusText = $"已删除本地项 {target.Name}，本地目录共 {itemCount} 项。";
            },
            cancellationToken);
    }

    public async Task RenameSelectedLocalAsync(CancellationToken cancellationToken = default)
    {
        BeginLocalInlineRename(SelectedLocalEntry);
        await Task.CompletedTask;
    }

    public void CancelLocalInlineRename(LocalFileEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        entry.IsEditingName = false;
        entry.EditingName = entry.Name;
    }

    public async Task CommitLocalInlineRenameAsync(LocalFileEntry? target, string? rawName, CancellationToken cancellationToken = default)
    {
        if (target is null || !target.IsEditingName || target.IsParentNavigation)
        {
            return;
        }

        target.IsEditingName = false;
        var newName = NormalizeEntryName(rawName);
        if (newName is null || string.Equals(target.Name, newName, StringComparison.Ordinal))
        {
            target.EditingName = target.Name;
            return;
        }

        if (!IsValidLocalEntryName(newName))
        {
            await _dialogService.ShowInfoAsync("重命名本地项", "名称无效，不能包含路径分隔符、保留名称或 Windows 不支持的字符。", cancellationToken);
            return;
        }

        var parentPath = Path.GetDirectoryName(target.FullPath);
        if (string.IsNullOrWhiteSpace(parentPath))
        {
            return;
        }

        var destinationPath = Path.Combine(parentPath, newName);
        if (!string.Equals(target.FullPath, destinationPath, StringComparison.OrdinalIgnoreCase)
            && (File.Exists(destinationPath) || Directory.Exists(destinationPath)))
        {
            await _dialogService.ShowInfoAsync("重命名本地项", $"“{newName}”已存在。", cancellationToken);
            return;
        }

        await ExecuteLocalBusyAsync(
            async innerCancellationToken =>
            {
                if (target.IsDirectory)
                {
                    Directory.Move(target.FullPath, destinationPath);
                }
                else
                {
                    File.Move(target.FullPath, destinationPath);
                }

                var itemCount = await LoadLocalEntriesCoreAsync(CurrentLocalPath, innerCancellationToken);
                await _dispatcher.InvokeAsync(() => SelectLocalEntryByName(newName));
                StatusText = $"已重命名本地项 {target.Name} -> {newName}，本地目录共 {itemCount} 项。";
            },
            cancellationToken);
    }

    public void CancelSelectedTransfers()
    {
        foreach (var entry in _selectedTransferLogs.ToArray())
        {
            CancelTransfer(entry);
        }

        OnPropertyChanged(nameof(CanCancelSelectedTransfers));
    }

    public void CancelAllTransfers()
    {
        List<TransferLogEntry> pendingEntries;
        lock (_transferQueueSync)
        {
            pendingEntries = _pendingTransfers.Select(item => item.LogEntry).ToList();
            _pendingTransfers.Clear();
            HasPendingTransfers = false;
        }

        foreach (var entry in pendingEntries)
        {
            CancelTransferLog(entry, "已放弃排队传输");
        }

        _activeTransferCts?.Cancel();
        UpdateTransferLogSummary();
    }

    public void ClearTransferLogs()
    {
        for (var index = TransferLogs.Count - 1; index >= 0; index--)
        {
            if (TransferLogs[index].IsCancelable)
            {
                continue;
            }

            TransferLogs.RemoveAt(index);
        }

        SetSelectedTransferLogs([]);
        UpdateTransferLogSummary();
        StatusText = TransferLogs.Count == 0
            ? "已清空传输记录。"
            : "已清空历史传输记录，进行中的任务已保留。";
        OnPropertyChanged(nameof(CanClearTransferLogs));
    }

    public async Task DeleteSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedEntry is null || SelectedEntry.IsParentNavigation)
        {
            return;
        }

        var target = SelectedEntry;
        var confirmed = await _dialogService.ConfirmDeleteAsync("删除远程项", $"确定要删除“{target.Name}”吗？", cancellationToken);
        if (!confirmed)
        {
            return;
        }

        await ExecuteRemoteBusyAsync(
            async innerCancellationToken =>
            {
                await _session.DeleteAsync(target.FullPath, target.IsDirectory, innerCancellationToken);
                var itemCount = await LoadRemoteEntriesCoreAsync(CurrentPath, innerCancellationToken);
                StatusText = $"已删除 {target.Name}，远程目录共 {itemCount} 项。";
            },
            cancellationToken);
    }

    public async Task RenameSelectedAsync(CancellationToken cancellationToken = default)
    {
        BeginRemoteInlineRename(SelectedEntry);
        await Task.CompletedTask;
    }

    public void CancelRemoteInlineRename(SftpEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        entry.IsEditingName = false;
        entry.EditingName = entry.Name;
    }

    public async Task CommitRemoteInlineRenameAsync(SftpEntry? target, string? rawName, CancellationToken cancellationToken = default)
    {
        if (target is null || !target.IsEditingName || target.IsParentNavigation)
        {
            return;
        }

        target.IsEditingName = false;
        var newName = NormalizeEntryName(rawName);
        if (newName is null || string.Equals(target.Name, newName, StringComparison.Ordinal))
        {
            target.EditingName = target.Name;
            return;
        }

        if (!IsValidRemoteEntryName(newName))
        {
            await _dialogService.ShowInfoAsync("重命名远程项", "名称无效，不能包含路径分隔符，也不能为 . 或 ..。", cancellationToken);
            return;
        }

        var destinationPath = RemotePathHelper.Combine(RemotePathHelper.GetParent(target.FullPath), newName);
        await ExecuteRemoteBusyAsync(
            async innerCancellationToken =>
            {
                await _session.RenameAsync(target.FullPath, destinationPath, innerCancellationToken);
                var itemCount = await LoadRemoteEntriesCoreAsync(CurrentPath, innerCancellationToken);
                await _dispatcher.InvokeAsync(() => SelectRemoteEntryByName(newName));
                StatusText = $"已重命名远程项 {target.Name} -> {newName}，远程目录共 {itemCount} 项。";
            },
            cancellationToken);
    }

    public async Task CreateDirectoryAsync(string directoryName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(directoryName))
        {
            return;
        }

        var trimmedName = directoryName.Trim();
        var remotePath = RemotePathHelper.Combine(CurrentPath, trimmedName);
        await ExecuteRemoteBusyAsync(
            async innerCancellationToken =>
            {
                await _session.CreateDirectoryAsync(remotePath, innerCancellationToken);
                var itemCount = await LoadRemoteEntriesCoreAsync(CurrentPath, innerCancellationToken);
                StatusText = $"已创建远程目录 {trimmedName}，远程目录共 {itemCount} 项。";
            },
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetimeCts.Cancel();
        _activeTransferCts?.Cancel();
        _session.ConnectionStateChanged -= OnSessionConnectionStateChanged;
        _session.TransferProgressChanged -= OnSessionTransferProgressChanged;
        await _session.DisposeAsync();
        _activeTransferCts?.Dispose();
        _lifetimeCts.Dispose();
    }

    private async Task ConnectAndLoadAsync(bool isRetry, CancellationToken cancellationToken)
    {
        if (_disposed || IsInitializing)
        {
            return;
        }

        using var linkedCts = CreateLinkedTokenSource(cancellationToken);
        var token = linkedCts.Token;

        IsInitializing = true;
        IsReady = false;
        OverlayVisibility = Visibility.Visible;
        RetryVisibility = Visibility.Collapsed;
        OverlayTitle = isRetry ? "正在重新连接 SFTP..." : "正在连接 SFTP...";
        OverlayMessage = "正在建立连接并加载远程目录，请稍候。";
        StatusText = isRetry ? "正在重新连接 SFTP..." : "正在连接 SFTP...";

        try
        {
            await _session.ConnectAsync(_request, token);
            CacheSecretIfNeeded();

            var workingDirectory = RemotePathHelper.NormalizeAbsolute(await _session.GetWorkingDirectoryAsync(token));
            var initialSelection = await ResolveInitialPathAsync(workingDirectory, token);
            var itemCount = await LoadRemoteEntriesCoreAsync(initialSelection.Path, token);

            OverlayVisibility = Visibility.Collapsed;
            RetryVisibility = Visibility.Collapsed;
            IsReady = true;
            StatusText = initialSelection.SuccessMessage ?? $"已加载 {itemCount} 个远程项。";
        }
        catch (OperationCanceledException) when (_disposed || _lifetimeCts.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (_disposed || _lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                Entries.Clear();
                SelectedEntry = null;
                RemoteSummaryText = "远程目录未连接";
            });

            IsReady = false;
            OverlayVisibility = Visibility.Visible;
            RetryVisibility = Visibility.Visible;
            OverlayTitle = "SFTP 连接失败";
            OverlayMessage = string.IsNullOrWhiteSpace(ex.Message)
                ? "建立 SFTP 连接失败。"
                : ex.Message;
            StatusText = $"连接失败: {OverlayMessage}";
        }
        finally
        {
            IsInitializing = false;
        }
    }

    private async Task<InitialPathSelection> ResolveInitialPathAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        CurrentPath = workingDirectory;

        if (string.IsNullOrWhiteSpace(Profile.StartupDirectory))
        {
            return new InitialPathSelection(workingDirectory, $"已连接，当前远程目录: {workingDirectory}");
        }

        var startupPath = RemotePathHelper.Resolve(workingDirectory, Profile.StartupDirectory);
        try
        {
            await _session.ListDirectoryAsync(startupPath, cancellationToken);
            return new InitialPathSelection(startupPath, $"已连接，当前远程目录: {startupPath}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new InitialPathSelection(
                workingDirectory,
                $"启动目录不可用，已切换到 {workingDirectory}。");
        }
    }

    private void EnqueueTransfer(TransferQueueItem item)
    {
        var shouldStartProcessor = false;
        lock (_transferQueueSync)
        {
            _pendingTransfers.Add(item);
            HasPendingTransfers = _pendingTransfers.Count > 0;
            if (!_isTransferProcessorRunning)
            {
                _isTransferProcessorRunning = true;
                shouldStartProcessor = true;
            }
        }

        if (shouldStartProcessor)
        {
            _ = ProcessTransferQueueAsync();
        }
    }

    private async Task ProcessTransferQueueAsync()
    {
        while (!_disposed)
        {
            TransferQueueItem? item;
            lock (_transferQueueSync)
            {
                if (_pendingTransfers.Count == 0)
                {
                    _isTransferProcessorRunning = false;
                    HasPendingTransfers = false;
                    return;
                }

                item = _pendingTransfers[0];
                _pendingTransfers.RemoveAt(0);
                HasPendingTransfers = _pendingTransfers.Count > 0;
            }

            if (item is not null)
            {
                await RunTransferAsync(item);
            }
        }
    }

    private async Task RunTransferAsync(TransferQueueItem item)
    {
        using var linkedCts = CreateLinkedTokenSource();
        _activeTransferCts = linkedCts;
        _activeTransferLogEntry = item.LogEntry;
        item.LogEntry.Status = "进行中";
        item.LogEntry.IsCancelable = true;
        IsTransferRunning = true;

        try
        {
            switch (item.Kind)
            {
                case TransferKind.Upload:
                    item.LogEntry.Message = item.IsDirectory
                        ? $"正在上传目录: {item.DisplayName}"
                        : $"正在上传: {item.DisplayName}";
                    await _session.UploadAsync(item.LocalPath, item.RemotePath, linkedCts.Token);
                    await TryRefreshRemoteAfterTransferAsync(linkedCts.Token);
                    StatusText = $"已上传 {item.DisplayName}";
                    CompleteTransferLog(item.LogEntry, $"已上传 {item.DisplayName}");
                    break;

                case TransferKind.Download:
                    item.LogEntry.Message = item.IsDirectory
                        ? $"正在下载目录: {item.DisplayName}"
                        : $"正在下载: {item.DisplayName}";
                    await _session.DownloadAsync(item.RemotePath, item.LocalPath, linkedCts.Token);
                    await TryRefreshLocalAfterTransferAsync(linkedCts.Token);
                    StatusText = $"已下载 {item.DisplayName}";
                    CompleteTransferLog(item.LogEntry, $"已下载 {item.DisplayName}");
                    break;
            }
        }
        catch (OperationCanceledException) when (_disposed || _lifetimeCts.IsCancellationRequested)
        {
            CancelTransferLog(item.LogEntry, "传输已结束");
        }
        catch (OperationCanceledException)
        {
            CancelTransferLog(item.LogEntry, "已放弃传输");
            StatusText = $"已取消传输 {item.DisplayName}";
        }
        catch (Exception ex)
        {
            FailTransferLog(item.LogEntry, ex.Message);
            StatusText = $"传输失败: {ex.Message}";
        }
        finally
        {
            item.LogEntry.IsCancelable = false;
            if (ReferenceEquals(_activeTransferLogEntry, item.LogEntry))
            {
                _activeTransferLogEntry = null;
            }

            _activeTransferCts?.Dispose();
            _activeTransferCts = null;
            IsTransferRunning = false;
            OnPropertyChanged(nameof(CanCancelSelectedTransfers));
            UpdateTransferLogSummary();
        }
    }

    private async Task TryRefreshRemoteAfterTransferAsync(CancellationToken cancellationToken)
    {
        if (_disposed || !IsReady)
        {
            return;
        }

        await ExecuteRemoteBusyAsync(
            async innerCancellationToken =>
            {
                await LoadRemoteEntriesCoreAsync(CurrentPath, innerCancellationToken);
            },
            cancellationToken,
            showErrorDialog: false,
            allowDuringTransferQueue: true);
    }

    private async Task TryRefreshLocalAfterTransferAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return;
        }

        await ExecuteLocalBusyAsync(
            async innerCancellationToken =>
            {
                await LoadLocalEntriesCoreAsync(CurrentLocalPath, innerCancellationToken);
            },
            cancellationToken,
            showErrorDialog: false);
    }

    private void CancelTransfer(TransferLogEntry entry)
    {
        if (!entry.IsCancelable)
        {
            return;
        }

        var removedFromQueue = false;
        lock (_transferQueueSync)
        {
            var queueIndex = _pendingTransfers.FindIndex(item => item.LogEntry.Id == entry.Id);
            if (queueIndex >= 0)
            {
                _pendingTransfers.RemoveAt(queueIndex);
                HasPendingTransfers = _pendingTransfers.Count > 0;
                removedFromQueue = true;
            }
        }

        if (removedFromQueue)
        {
            CancelTransferLog(entry, "已放弃排队传输");
            UpdateTransferLogSummary();
            return;
        }

        if (ReferenceEquals(_activeTransferLogEntry, entry))
        {
            _activeTransferCts?.Cancel();
        }
    }

    private async Task<int> LoadRemoteEntriesCoreAsync(string targetPath, CancellationToken cancellationToken)
    {
        var normalizedPath = RemotePathHelper.NormalizeAbsolute(targetPath);
        var items = await _session.ListDirectoryAsync(normalizedPath, cancellationToken);
        var visibleItems = new List<SftpEntry>();

        if (normalizedPath != "/")
        {
            visibleItems.Add(new SftpEntry
            {
                Name = "..",
                FullPath = RemotePathHelper.GetParent(normalizedPath),
                IsDirectory = true,
                IsParentNavigation = true,
            });
        }

        visibleItems.AddRange(items);

        await _dispatcher.InvokeAsync(() =>
        {
            CurrentPath = normalizedPath;
            Entries.Clear();
            foreach (var item in visibleItems)
            {
                Entries.Add(item);
            }

            SelectedEntry = null;
            RemoteSummaryText = BuildRemoteSummary(items);
        });

        return items.Count;
    }

    private async Task<int> LoadLocalEntriesAsync(string targetPath, CancellationToken cancellationToken, string? successStatus = null)
    {
        var itemCount = 0;
        await ExecuteLocalBusyAsync(
            async innerCancellationToken =>
            {
                itemCount = await LoadLocalEntriesCoreAsync(targetPath, innerCancellationToken);
                StatusText = successStatus ?? $"已加载本地目录，共 {itemCount} 项。";
            },
            cancellationToken);

        return itemCount;
    }

    private async Task<int> LoadLocalEntriesCoreAsync(string targetPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedPath = NormalizeLocalPath(targetPath);
        if (!Directory.Exists(normalizedPath))
        {
            throw new DirectoryNotFoundException($"本地目录不存在: {normalizedPath}");
        }

        var items = new List<LocalFileEntry>();
        if (HasLocalParent(normalizedPath))
        {
            items.Add(new LocalFileEntry
            {
                Name = "..",
                FullPath = GetLocalParent(normalizedPath),
                IsDirectory = true,
                IsParentNavigation = true,
            });
        }

        var directories = Directory.EnumerateDirectories(normalizedPath)
            .Select(path => new DirectoryInfo(path))
            .OrderBy(info => info.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var directory in directories)
        {
            items.Add(new LocalFileEntry
            {
                Name = directory.Name,
                FullPath = directory.FullName,
                IsDirectory = true,
                LastWriteTime = directory.LastWriteTime,
            });
        }

        var files = Directory.EnumerateFiles(normalizedPath)
            .Select(path => new FileInfo(path))
            .OrderBy(info => info.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            items.Add(new LocalFileEntry
            {
                Name = file.Name,
                FullPath = file.FullName,
                IsDirectory = false,
                Size = file.Length,
                LastWriteTime = file.LastWriteTime,
            });
        }

        await _dispatcher.InvokeAsync(() =>
        {
            CurrentLocalPath = normalizedPath;
            LocalEntries.Clear();
            foreach (var item in items)
            {
                LocalEntries.Add(item);
            }

            SelectedLocalEntry = null;
            LocalSummaryText = BuildLocalSummary(items);
        });

        return items.Count(item => !item.IsParentNavigation);
    }

    private void BeginLocalInlineRename(LocalFileEntry? entry)
    {
        if (entry is null || entry.IsParentNavigation || !CanRenameLocalSelection)
        {
            return;
        }

        CancelAllInlineRenames();
        entry.EditingName = entry.Name;
        entry.IsEditingName = true;
    }

    private void BeginRemoteInlineRename(SftpEntry? entry)
    {
        if (entry is null || entry.IsParentNavigation || !CanRenameSelection)
        {
            return;
        }

        CancelAllInlineRenames();
        entry.EditingName = entry.Name;
        entry.IsEditingName = true;
    }

    private void CancelAllInlineRenames()
    {
        foreach (var entry in LocalEntries)
        {
            entry.IsEditingName = false;
            entry.EditingName = entry.Name;
        }

        foreach (var entry in Entries)
        {
            entry.IsEditingName = false;
            entry.EditingName = entry.Name;
        }
    }

    private void SelectLocalEntryByName(string name)
    {
        SelectedLocalEntry = LocalEntries.FirstOrDefault(entry =>
            !entry.IsParentNavigation &&
            string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private void SelectRemoteEntryByName(string name)
    {
        SelectedEntry = Entries.FirstOrDefault(entry =>
            !entry.IsParentNavigation &&
            string.Equals(entry.Name, name, StringComparison.Ordinal))
            ?? Entries.FirstOrDefault(entry =>
                !entry.IsParentNavigation &&
                string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private Task ExecuteRemoteBusyAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken,
        string errorTitle = "SFTP 操作失败",
        Action<Exception>? onError = null,
        bool showErrorDialog = true,
        bool allowDuringTransferQueue = false)
    {
        return ExecuteBusyAsync(
            action,
            cancellationToken,
            usesRemoteSession: true,
            requiresReady: true,
            errorTitle,
            onError,
            showErrorDialog,
            allowDuringTransferQueue);
    }

    private Task ExecuteLocalBusyAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken,
        string errorTitle = "本地文件操作失败",
        bool showErrorDialog = true)
    {
        return ExecuteBusyAsync(
            action,
            cancellationToken,
            usesRemoteSession: false,
            requiresReady: false,
            errorTitle,
            onError: null,
            showErrorDialog,
            allowDuringTransferQueue: false);
    }

    private async Task ExecuteBusyAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken,
        bool usesRemoteSession,
        bool requiresReady,
        string errorTitle,
        Action<Exception>? onError,
        bool showErrorDialog,
        bool allowDuringTransferQueue)
    {
        if (_disposed || IsInitializing || (requiresReady && !IsReady))
        {
            return;
        }

        if (usesRemoteSession)
        {
            if (IsRemoteBusy || (!allowDuringTransferQueue && (IsTransferRunning || HasPendingTransfers)))
            {
                return;
            }

            IsRemoteBusy = true;
        }
        else
        {
            if (IsLocalBusy)
            {
                return;
            }

            IsLocalBusy = true;
        }

        using var linkedCts = CreateLinkedTokenSource(cancellationToken);
        var token = linkedCts.Token;

        try
        {
            await action(token);
        }
        catch (OperationCanceledException) when (_disposed || _lifetimeCts.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (_disposed || _lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex);
            StatusText = $"{errorTitle}: {ex.Message}";
            if (showErrorDialog)
            {
                await _dialogService.ShowErrorAsync(errorTitle, ex.Message, token);
            }
        }
        finally
        {
            if (usesRemoteSession)
            {
                IsRemoteBusy = false;
            }
            else
            {
                IsLocalBusy = false;
            }
        }
    }

    private async Task<bool> EnsureLocalFileTargetReadyAsync(string localPath, CancellationToken cancellationToken)
    {
        var directoryPath = Path.GetDirectoryName(localPath);
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            directoryPath = CurrentLocalPath;
        }

        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        if (!File.Exists(localPath))
        {
            return true;
        }

        return await _dialogService.ConfirmDeleteAsync(
            "覆盖本地文件",
            $"本地文件“{Path.GetFileName(localPath)}”已存在，是否覆盖？",
            cancellationToken);
    }

    private async void OnSessionConnectionStateChanged(object? sender, SftpConnectionStateChangedEventArgs e)
    {
        await _dispatcher.InvokeAsync(() =>
        {
            if (e.State == SftpConnectionState.Connected)
            {
                CacheSecretIfNeeded();
            }

            StatusText = e.State switch
            {
                SftpConnectionState.Connecting => "正在连接 SFTP...",
                SftpConnectionState.Connected => "SFTP 已连接。",
                SftpConnectionState.Reconnecting => "正在重新连接 SFTP...",
                SftpConnectionState.Faulted => $"连接异常: {e.Message}",
                SftpConnectionState.Disconnected => "SFTP 已断开。",
                _ => e.Message ?? "就绪",
            };
        });
    }

    private async void OnSessionTransferProgressChanged(object? sender, SftpTransferProgressChangedEventArgs e)
    {
        await _dispatcher.InvokeAsync(() =>
        {
            if (_activeTransferLogEntry is null)
            {
                return;
            }

            _activeTransferLogEntry.Status = "进行中";
            _activeTransferLogEntry.TotalBytes = e.TotalBytes;
            _activeTransferLogEntry.ProgressPercent = e.TotalBytes > 0
                ? Math.Clamp((double)e.BytesTransferred / e.TotalBytes * 100d, 0d, 100d)
                : 0d;
            _activeTransferLogEntry.ProgressText = BuildProgressText(e.BytesTransferred, e.TotalBytes);
            _activeTransferLogEntry.FileSizeText = e.TotalBytes > 0
                ? FormatBytes(e.TotalBytes)
                : "--";
            _activeTransferLogEntry.TransferSpeedText = BuildTransferSpeedText(_activeTransferLogEntry, e.BytesTransferred);
            _activeTransferLogEntry.Message = BuildProgressMessage(e);
        });
    }

    private void CacheSecretIfNeeded()
    {
        if (_secretCached)
        {
            return;
        }

        if (Profile.AuthType == AuthenticationType.Password && string.IsNullOrWhiteSpace(_request.Secret))
        {
            return;
        }

        _sessionSecretCache.StoreSecret(Profile.Id, string.IsNullOrWhiteSpace(_request.Secret) ? null : _request.Secret);
        _secretCached = true;
    }

    private TransferLogEntry StartTransferLog(string direction, string localPath, string remotePath, string message)
    {
        var entry = new TransferLogEntry
        {
            Timestamp = DateTime.Now,
            TransferStartedAt = DateTime.Now,
            Direction = direction,
            LocalPath = localPath,
            RemotePath = remotePath,
            Status = "排队中",
            Message = message,
            ProgressText = "0%",
            FileSizeText = "--",
            TransferSpeedText = "--",
            ProgressPercent = 0d,
            IsCancelable = true,
        };

        TransferLogs.Insert(0, entry);
        TrimTransferLogs();
        UpdateTransferLogSummary();
        OnPropertyChanged(nameof(CanClearTransferLogs));
        return entry;
    }

    private void CompleteTransferLog(TransferLogEntry entry, string message)
    {
        entry.Status = "成功";
        entry.Message = message;
        entry.ProgressText = "100%";
        entry.ProgressPercent = 100d;
        if (entry.TotalBytes > 0)
        {
            entry.FileSizeText = FormatBytes(entry.TotalBytes);
        }

        entry.IsCancelable = false;
        if (ReferenceEquals(_activeTransferLogEntry, entry))
        {
            _activeTransferLogEntry = null;
        }
    }

    private void FailTransferLog(TransferLogEntry entry, string message)
    {
        entry.Status = "失败";
        entry.Message = message;
        entry.TransferSpeedText = "--";
        entry.IsCancelable = false;
        if (string.IsNullOrWhiteSpace(entry.ProgressText))
        {
            entry.ProgressText = "0%";
        }

        if (entry.ProgressPercent < 0)
        {
            entry.ProgressPercent = 0d;
        }

        if (ReferenceEquals(_activeTransferLogEntry, entry))
        {
            _activeTransferLogEntry = null;
        }
    }

    private void CancelTransferLog(TransferLogEntry entry, string message)
    {
        entry.Status = "已取消";
        entry.Message = message;
        entry.TransferSpeedText = "--";
        entry.IsCancelable = false;
        if (string.IsNullOrWhiteSpace(entry.ProgressText))
        {
            entry.ProgressText = "0%";
        }

        if (ReferenceEquals(_activeTransferLogEntry, entry))
        {
            _activeTransferLogEntry = null;
        }
    }

    private void TrimTransferLogs()
    {
        while (TransferLogs.Count > 200)
        {
            TransferLogs.RemoveAt(TransferLogs.Count - 1);
        }
    }

    private void UpdateTransferLogSummary()
    {
        var activeCount = IsTransferRunning ? 1 : 0;
        int pendingCount;
        lock (_transferQueueSync)
        {
            pendingCount = _pendingTransfers.Count;
        }

        TransferLogSummaryText = TransferLogs.Count == 0
            ? "暂无传输记录"
            : $"传输日志: {TransferLogs.Count} 条，进行中 {activeCount}，排队 {pendingCount}";
        OnPropertyChanged(nameof(CanClearTransferLogs));
    }

    private static string BuildProgressText(long bytesTransferred, long totalBytes)
    {
        if (totalBytes <= 0)
        {
            return FormatBytes(bytesTransferred);
        }

        var percent = Math.Clamp((double)bytesTransferred / totalBytes * 100d, 0d, 100d);
        return $"{percent:0}% ({FormatBytes(bytesTransferred)} / {FormatBytes(totalBytes)})";
    }

    private static string BuildProgressMessage(SftpTransferProgressChangedEventArgs progress)
    {
        var itemPath = string.IsNullOrWhiteSpace(progress.CurrentItemPath)
            ? progress.RemotePath
            : progress.CurrentItemPath;

        var itemName = Path.GetFileName(itemPath.TrimEnd('/', '\\'));
        if (string.IsNullOrWhiteSpace(itemName))
        {
            itemName = itemPath;
        }

        return progress.IsDirectoryTransfer
            ? $"正在传输目录内容: {itemName}"
            : $"正在传输: {itemName}";
    }

    private static string BuildTransferSpeedText(TransferLogEntry entry, long bytesTransferred)
    {
        var now = DateTime.Now;
        var previousTimestamp = entry.LastProgressAt ?? entry.TransferStartedAt;
        var previousBytes = entry.LastProgressAt.HasValue ? entry.LastBytesTransferred : 0L;
        var elapsedSeconds = (now - previousTimestamp).TotalSeconds;

        entry.LastProgressAt = now;
        entry.LastBytesTransferred = bytesTransferred;

        if (elapsedSeconds <= 0.05d)
        {
            return entry.TransferSpeedText == "--" ? "计算中..." : entry.TransferSpeedText;
        }

        var bytesPerSecond = Math.Max(0d, (bytesTransferred - previousBytes) / elapsedSeconds);
        return $"{FormatBytes((long)bytesPerSecond)}/s";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unitIndex = 0;
        while (value >= 1024d && unitIndex < units.Length - 1)
        {
            value /= 1024d;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }

    private CancellationTokenSource CreateLinkedTokenSource(CancellationToken cancellationToken = default)
    {
        return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts.Token);
    }

    private void NotifyOperationStateChanged()
    {
        OnPropertyChanged(nameof(CanInteractLocal));
        OnPropertyChanged(nameof(CanInteractRemote));
        OnPropertyChanged(nameof(CanSelectRemoteEntries));
        OnPropertyChanged(nameof(CanQueueTransfers));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(CanNavigateRemoteUp));
        OnPropertyChanged(nameof(CanNavigateLocalUp));
        OnPropertyChanged(nameof(CanUploadSelection));
        OnPropertyChanged(nameof(CanDownloadSelection));
        OnPropertyChanged(nameof(CanDeleteLocalSelection));
        OnPropertyChanged(nameof(CanDeleteSelection));
        OnPropertyChanged(nameof(CanRenameLocalSelection));
        OnPropertyChanged(nameof(CanRenameSelection));
        OnPropertyChanged(nameof(CanCancelSelectedTransfers));
        OnPropertyChanged(nameof(CanCancelAllTransfers));
        OnPropertyChanged(nameof(CanClearTransferLogs));
    }

    private string ResolveRemoteInputPath(string? rawInput)
    {
        var candidate = (rawInput ?? string.Empty).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(candidate) || candidate == ".")
        {
            return CurrentPath;
        }

        if (candidate == "..")
        {
            return RemotePathHelper.GetParent(CurrentPath);
        }

        return RemotePathHelper.Resolve(CurrentPath, candidate);
    }

    private string ResolveLocalInputPath(string? rawInput)
    {
        var candidate = (rawInput ?? string.Empty).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(candidate) || candidate == ".")
        {
            return CurrentLocalPath;
        }

        if (candidate == "..")
        {
            return GetLocalParent(CurrentLocalPath);
        }

        return Path.IsPathRooted(candidate)
            ? NormalizeLocalPath(candidate)
            : NormalizeLocalPath(Path.Combine(CurrentLocalPath, candidate));
    }

    private static string BuildRemoteSummary(IEnumerable<SftpEntry> entries)
    {
        var folders = entries.Count(entry => entry.IsDirectory);
        var files = entries.Count(entry => !entry.IsDirectory);
        var totalBytes = entries.Where(entry => !entry.IsDirectory).Sum(entry => entry.Size);
        return $"{files} 个文件，{folders} 个目录，大小总计: {FormatBytes(totalBytes)}";
    }

    private static string BuildLocalSummary(IEnumerable<LocalFileEntry> entries)
    {
        var folders = entries.Count(entry => entry.IsDirectory && !entry.IsParentNavigation);
        var files = entries.Count(entry => !entry.IsDirectory && !entry.IsParentNavigation);
        return $"{folders} 个文件夹，{files} 个文件";
    }

    private static string? NormalizeEntryName(string? rawName)
    {
        var candidate = (rawName ?? string.Empty).Trim();
        return candidate.Length == 0 ? null : candidate;
    }

    private static bool IsValidLocalEntryName(string name)
    {
        return name is not "." and not ".."
            && !name.Contains(Path.DirectorySeparatorChar)
            && !name.Contains(Path.AltDirectorySeparatorChar)
            && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    private static bool IsValidRemoteEntryName(string name)
    {
        return name is not "." and not ".."
            && !name.Contains('/')
            && !name.Contains('\\');
    }

    private static bool HasLocalParent(string path)
    {
        var normalizedPath = NormalizeLocalPath(path);
        var root = Path.GetPathRoot(normalizedPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        return !string.Equals(
            normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string GetLocalParent(string path)
    {
        var normalizedPath = NormalizeLocalPath(path);
        return Directory.GetParent(normalizedPath)?.FullName
            ?? Path.GetPathRoot(normalizedPath)
            ?? normalizedPath;
    }

    private static string NormalizeLocalPath(string path)
    {
        var candidate = string.IsNullOrWhiteSpace(path)
            ? GetDefaultLocalPath()
            : path.Trim().Trim('"');

        return Path.GetFullPath(candidate);
    }

    private static string GetDefaultLocalPath()
    {
        var candidates = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };

        return candidates.FirstOrDefault(Directory.Exists)
            ?? Environment.CurrentDirectory;
    }

    private static string ResolveInitialLocalPath(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var normalized = NormalizeLocalPath(configuredPath);
            if (Directory.Exists(normalized))
            {
                return normalized;
            }
        }

        return GetDefaultLocalPath();
    }

    private void RefreshLocalDrives()
    {
        var availableDrives = DriveInfo.GetDrives()
            .Select(drive => drive.Name)
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!LocalDrives.SequenceEqual(availableDrives, StringComparer.OrdinalIgnoreCase))
        {
            LocalDrives.Clear();
            foreach (var drive in availableDrives)
            {
                LocalDrives.Add(drive);
            }
        }

        var currentDriveRoot = GetLocalDriveRoot(CurrentLocalPath);
        if (!string.IsNullOrWhiteSpace(currentDriveRoot) &&
            availableDrives.Contains(currentDriveRoot, StringComparer.OrdinalIgnoreCase))
        {
            SelectedLocalDrive = availableDrives.First(drive => string.Equals(drive, currentDriveRoot, StringComparison.OrdinalIgnoreCase));
            return;
        }

        SelectedLocalDrive = null;
    }

    private static string? GetLocalDriveRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var normalizedPath = NormalizeLocalPath(path);
        var root = Path.GetPathRoot(normalizedPath);
        return string.IsNullOrWhiteSpace(root) ? null : root;
    }

    private static string NormalizeLocalDriveRoot(string driveRoot)
    {
        var normalizedRoot = GetLocalDriveRoot(driveRoot);
        return string.IsNullOrWhiteSpace(normalizedRoot)
            ? string.Empty
            : normalizedRoot;
    }

    private static SessionProfile CloneProfile(SessionProfile source)
    {
        return new SessionProfile
        {
            Id = source.Id,
            Name = source.Name,
            GroupPath = source.GroupPath,
            Host = source.Host,
            Port = source.Port,
            Username = source.Username,
            AuthType = source.AuthType,
            PrivateKeyPath = source.PrivateKeyPath,
            TerminalTheme = source.TerminalTheme,
            LocalStartupDirectory = source.LocalStartupDirectory,
            StartupDirectory = source.StartupDirectory,
            KeepAliveSeconds = source.KeepAliveSeconds,
            FontSize = source.FontSize,
            TerminalFontFamily = source.TerminalFontFamily,
            ScrollbackLines = source.ScrollbackLines,
            IsFavorite = source.IsFavorite,
            RememberSecret = source.RememberSecret,
        };
    }

    private sealed record InitialPathSelection(string Path, string? SuccessMessage);

    private enum TransferKind
    {
        Upload,
        Download,
    }

    private sealed record TransferQueueItem(
        TransferKind Kind,
        string LocalPath,
        string RemotePath,
        bool IsDirectory,
        string DisplayName,
        TransferLogEntry LogEntry);
}
