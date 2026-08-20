using System.Collections.ObjectModel;
using System.Windows.Threading;
using WiseShell.App.Infrastructure;
using WiseShell.App.Services;
using WiseShell.Core.Infrastructure;
using WiseShell.Core.Interfaces;
using WiseShell.Core.Models;

namespace WiseShell.App.ViewModels;

public sealed class RemoteDirectoryPickerViewModel : ObservableObject, IAsyncDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly IDialogService _dialogService;
    private readonly ISftpSession _session;
    private readonly SftpConnectionRequest _request;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private string _currentPath = "/";
    private string _pathInput = "/";
    private string _statusText = "请选择远程目录。";
    private bool _isBusy;
    private SftpEntry? _selectedEntry;
    private bool _disposed;

    public RemoteDirectoryPickerViewModel(
        Dispatcher dispatcher,
        SftpConnectionRequest request,
        ISftpSessionFactory sessionFactory,
        IDialogService dialogService)
    {
        _dispatcher = dispatcher;
        _dialogService = dialogService;
        _request = request;
        _session = sessionFactory.CreateSession();
    }

    public ObservableCollection<SftpEntry> Entries { get; } = new();

    public string CurrentPath
    {
        get => _currentPath;
        private set
        {
            if (SetProperty(ref _currentPath, value))
            {
                PathInput = value;
                OnPropertyChanged(nameof(CanNavigateUp));
            }
        }
    }

    public string PathInput
    {
        get => _pathInput;
        set => SetProperty(ref _pathInput, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanNavigateUp));
            }
        }
    }

    public bool CanNavigateUp => !IsBusy && CurrentPath != "/";

    public SftpEntry? SelectedEntry
    {
        get => _selectedEntry;
        set => SetProperty(ref _selectedEntry, value);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts.Token);
        var token = linkedCts.Token;

        await _session.ConnectAsync(_request, token);
        var workingDirectory = RemotePathHelper.NormalizeAbsolute(await _session.GetWorkingDirectoryAsync(token));
        var initialPath = string.IsNullOrWhiteSpace(_request.Profile.StartupDirectory)
            ? workingDirectory
            : RemotePathHelper.Resolve(workingDirectory, _request.Profile.StartupDirectory);

        try
        {
            await LoadDirectoryAsync(initialPath, token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await LoadDirectoryAsync(workingDirectory, token);
            StatusText = $"启动目录不可用，已回退到 {workingDirectory}。";
        }
    }

    public Task NavigateUpAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentPath == "/")
        {
            return Task.CompletedTask;
        }

        return LoadDirectoryAsync(RemotePathHelper.GetParent(CurrentPath), cancellationToken);
    }

    public Task NavigateIntoAsync(SftpEntry? entry, CancellationToken cancellationToken = default)
    {
        if (entry is null || !entry.IsDirectory)
        {
            return Task.CompletedTask;
        }

        var targetPath = entry.IsParentNavigation
            ? entry.FullPath
            : RemotePathHelper.Resolve(CurrentPath, entry.FullPath);
        return LoadDirectoryAsync(targetPath, cancellationToken);
    }

    public Task NavigateToInputAsync(CancellationToken cancellationToken = default)
    {
        var candidate = (PathInput ?? string.Empty).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(candidate) || candidate == ".")
        {
            candidate = CurrentPath;
        }
        else if (candidate == "..")
        {
            candidate = RemotePathHelper.GetParent(CurrentPath);
        }
        else
        {
            candidate = RemotePathHelper.Resolve(CurrentPath, candidate);
        }

        return LoadDirectoryAsync(candidate, cancellationToken);
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        return LoadDirectoryAsync(CurrentPath, cancellationToken);
    }

    public string GetSelectedPath()
    {
        if (SelectedEntry is { IsDirectory: true, IsParentNavigation: false })
        {
            return RemotePathHelper.Resolve(CurrentPath, SelectedEntry.FullPath);
        }

        return CurrentPath;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetimeCts.Cancel();
        await _session.DisposeAsync();
        _lifetimeCts.Dispose();
    }

    private async Task LoadDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        if (_disposed || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var normalizedPath = RemotePathHelper.NormalizeAbsolute(path);
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

            visibleItems.AddRange(items.Where(item => item.IsDirectory).OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase));

            await _dispatcher.InvokeAsync(() =>
            {
                CurrentPath = normalizedPath;
                Entries.Clear();
                foreach (var item in visibleItems)
                {
                    Entries.Add(item);
                }

                SelectedEntry = null;
                StatusText = $"当前目录: {normalizedPath}";
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusText = $"加载目录失败: {ex.Message}";
            await _dialogService.ShowErrorAsync("远程目录浏览失败", ex.Message, cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
