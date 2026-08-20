using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using WiseShell.App.Infrastructure;
using WiseShell.App.Services;
using WiseShell.Core.Enums;
using WiseShell.Core.Infrastructure;
using WiseShell.Core.Interfaces;
using WiseShell.Core.Models;

namespace WiseShell.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly ISessionRepository _sessionRepository;
    private readonly ICredentialStore _credentialStore;
    private readonly ISessionSecretCache _sessionSecretCache;
    private readonly IDialogService _dialogService;
    private readonly ISftpSessionFactory _sftpSessionFactory;
    private readonly Func<ITerminalSession> _terminalSessionFactory;
    private readonly Func<SftpConnectionRequest, Task> _openSftpBrowserAsync;
    private readonly ObservableCollection<SessionProfile> _profiles = new();
    private readonly HashSet<string> _folderPaths = new(StringComparer.OrdinalIgnoreCase);
    private TerminalDefaultsSettings _terminalDefaults = new();
    private SessionExplorerItemViewModel? _selectedExplorerItem;
    private TerminalTabViewModel? _selectedTab;

    public MainWindowViewModel(
        Dispatcher dispatcher,
        ISessionRepository sessionRepository,
        ICredentialStore credentialStore,
        ISessionSecretCache sessionSecretCache,
        IDialogService dialogService,
        ISftpSessionFactory sftpSessionFactory,
        Func<ITerminalSession> terminalSessionFactory,
        Func<SftpConnectionRequest, Task> openSftpBrowserAsync)
    {
        _dispatcher = dispatcher;
        _sessionRepository = sessionRepository;
        _credentialStore = credentialStore;
        _sessionSecretCache = sessionSecretCache;
        _dialogService = dialogService;
        _sftpSessionFactory = sftpSessionFactory;
        _terminalSessionFactory = terminalSessionFactory;
        _openSftpBrowserAsync = openSftpBrowserAsync;

        NewSessionCommand = new AsyncRelayCommand(CreateSessionAsync);
        EditSessionCommand = new AsyncRelayCommand(EditSelectedSessionAsync, () => SelectedSession is not null);
        RenameSessionCommand = new AsyncRelayCommand(RenameSelectedSessionAsync, () => SelectedSession is not null);
        DuplicateSessionCommand = new AsyncRelayCommand(DuplicateSelectedSessionAsync, () => SelectedSession is not null);
        DeleteSessionCommand = new AsyncRelayCommand(DeleteSelectedSessionAsync, () => SelectedSession is not null);
        ConnectSessionCommand = new AsyncRelayCommand(ConnectSelectedSessionAsync, () => SelectedSession is not null);
        DisconnectSessionCommand = new AsyncRelayCommand(DisconnectSelectedTabAsync, () => SelectedTab is not null);
        ReconnectSessionCommand = new AsyncRelayCommand(ReconnectSelectedTabAsync, () => SelectedTab is not null);
        RenameTabCommand = new AsyncRelayCommand(RenameSelectedTabAsync, () => SelectedTab is not null);
        OpenSftpCommand = new AsyncRelayCommand(OpenSelectedSftpAsync, () => ActiveSession is not null);
        RefreshSessionsCommand = new AsyncRelayCommand(RefreshSessionsAsync);
        NewFolderCommand = new AsyncRelayCommand(CreateFolderAsync);
        RenameFolderCommand = new AsyncRelayCommand(RenameSelectedFolderAsync, () => SelectedExplorerItem?.CanManageFolder == true);
        DeleteFolderCommand = new AsyncRelayCommand(DeleteSelectedFolderAsync, () => SelectedExplorerItem?.CanManageFolder == true);
        EditTerminalDefaultsCommand = new AsyncRelayCommand(EditTerminalDefaultsAsync);
    }

    public ObservableCollection<SessionExplorerItemViewModel> SessionTree { get; } = new();

    public ObservableCollection<TerminalTabViewModel> Tabs { get; } = new();

    public AsyncRelayCommand NewSessionCommand { get; }

    public AsyncRelayCommand EditSessionCommand { get; }

    public AsyncRelayCommand RenameSessionCommand { get; }

    public AsyncRelayCommand DuplicateSessionCommand { get; }

    public AsyncRelayCommand DeleteSessionCommand { get; }

    public AsyncRelayCommand ConnectSessionCommand { get; }

    public AsyncRelayCommand DisconnectSessionCommand { get; }

    public AsyncRelayCommand ReconnectSessionCommand { get; }

    public AsyncRelayCommand RenameTabCommand { get; }

    public AsyncRelayCommand OpenSftpCommand { get; }

    public AsyncRelayCommand RefreshSessionsCommand { get; }

    public AsyncRelayCommand NewFolderCommand { get; }

    public AsyncRelayCommand RenameFolderCommand { get; }

    public AsyncRelayCommand DeleteFolderCommand { get; }

    public AsyncRelayCommand EditTerminalDefaultsCommand { get; }

    public string WindowTitle => "WiseShell V1.0";

    public SessionExplorerItemViewModel? SelectedExplorerItem
    {
        get => _selectedExplorerItem;
        set
        {
            if (SetProperty(ref _selectedExplorerItem, value))
            {
                NotifySelectionPropertiesChanged();
                NotifyCommandStates();
            }
        }
    }

    public TerminalTabViewModel? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (SetProperty(ref _selectedTab, value))
            {
                NotifySelectionPropertiesChanged();
                NotifyCommandStates();
            }
        }
    }

    public SessionProfile? SelectedSession => SelectedExplorerItem?.Profile;

    public SessionProfile? ActiveSession => SelectedTab?.Profile ?? SelectedSession;

    public string SelectedItemNameText => SelectedExplorerItem?.DisplayName ?? "--";

    public string SelectedItemTypeText => SelectedExplorerItem switch
    {
        null => "--",
        { IsVirtualRoot: true } => "根节点",
        { IsFolder: true } => "文件夹",
        _ => "SSH 会话",
    };

    public string SelectedItemEndpointText => SelectedSession is null
        ? "--"
        : $"{SelectedSession.Host}:{SelectedSession.Port}";

    public string SelectedItemUserText => string.IsNullOrWhiteSpace(SelectedSession?.Username)
        ? "--"
        : SelectedSession.Username;

    public string SelectedItemGroupText => SelectedExplorerItem?.IsFolder == true
        ? (string.IsNullOrWhiteSpace(SelectedExplorerItem.FolderPath) ? "--" : SelectedExplorerItem.FolderPath)
        : string.IsNullOrWhiteSpace(SelectedSession?.GroupPath)
            ? "--"
            : SessionFolderPath.Normalize(SelectedSession.GroupPath);

    public string SelectedItemAuthenticationText => SelectedSession?.AuthType switch
    {
        AuthenticationType.Password => "密码",
        AuthenticationType.PrivateKey => "私钥",
        _ => "--",
    };

    public string SelectedItemChildrenText => SelectedExplorerItem is null
        ? "--"
        : SelectedExplorerItem.Children.Count.ToString();

    public string StatusBarSessionText => SelectedTab is not null
        ? $"ssh://{SelectedTab.SessionAddress}"
        : SelectedSession is not null
            ? $"会话: {SelectedSession.Name}"
            : "未连接";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await RefreshSessionsAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var tab in Tabs.ToList())
        {
            await tab.DisposeAsync();
        }

        _sessionSecretCache.Clear();
    }

    public void SelectExplorerItem(SessionExplorerItemViewModel? item)
    {
        SelectedExplorerItem = item;
    }

    public bool IsSessionsRootItem(SessionExplorerItemViewModel? item)
    {
        return item is not null &&
               item.IsVirtualRoot &&
               ReferenceEquals(item, FindRootSessionsItem());
    }

    public bool IsFavoritesRootItem(SessionExplorerItemViewModel? item)
    {
        return item is not null &&
               item.IsVirtualRoot &&
               !ReferenceEquals(item, FindRootSessionsItem());
    }

    public bool CanStartDrag(SessionExplorerItemViewModel? item)
    {
        return item is not null &&
               !item.IsVirtualRoot &&
               (item.Profile is not null || item.CanManageFolder);
    }

    public bool CanDropExplorerItem(SessionExplorerItemViewModel? sourceItem, SessionExplorerItemViewModel? targetItem)
    {
        if (!CanStartDrag(sourceItem))
        {
            return false;
        }

        var targetFolderPath = ResolveDropTargetFolderPath(targetItem);
        if (targetFolderPath is null)
        {
            return false;
        }

        if (sourceItem!.Profile is not null)
        {
            return !string.Equals(
                SessionFolderPath.Normalize(sourceItem.Profile.GroupPath),
                targetFolderPath,
                StringComparison.OrdinalIgnoreCase);
        }

        var sourceFolderPath = sourceItem.FolderPath;
        if (string.IsNullOrWhiteSpace(sourceFolderPath))
        {
            return false;
        }

        if (SessionFolderPath.IsSameOrDescendant(targetFolderPath, sourceFolderPath))
        {
            return false;
        }

        var destinationFolderPath = SessionFolderPath.Combine(targetFolderPath, SessionFolderPath.GetName(sourceFolderPath));
        return !string.Equals(sourceFolderPath, destinationFolderPath, StringComparison.OrdinalIgnoreCase);
    }

    private void BeginInlineRename(SessionExplorerItemViewModel? item)
    {
        if (item is null || item.IsVirtualRoot || (item.Profile is null && !item.CanManageFolder))
        {
            return;
        }

        foreach (var root in SessionTree)
        {
            CancelInlineRenameRecursive(root);
        }

        item.EditingName = item.DisplayName;
        item.IsEditingName = true;
    }

    public async Task MoveExplorerItemAsync(
        SessionExplorerItemViewModel? sourceItem,
        SessionExplorerItemViewModel? targetItem,
        CancellationToken cancellationToken = default)
    {
        if (!CanDropExplorerItem(sourceItem, targetItem))
        {
            return;
        }

        var targetFolderPath = ResolveDropTargetFolderPath(targetItem)!;
        if (sourceItem!.Profile is not null)
        {
            sourceItem.Profile.GroupPath = targetFolderPath;
            await PersistWorkspaceAsync(cancellationToken);
            BuildSessionTree();
            ExpandFolderPath(targetFolderPath);
            SelectExplorerItem(FindSessionItem(sourceItem.Profile.Id));
            return;
        }

        var sourceFolderPath = sourceItem.FolderPath;
        var destinationFolderPath = SessionFolderPath.Combine(targetFolderPath, SessionFolderPath.GetName(sourceFolderPath));
        ReplaceFolderPrefix(sourceFolderPath, destinationFolderPath);
        await PersistWorkspaceAsync(cancellationToken);
        BuildSessionTree();
        ExpandFolderPath(SessionFolderPath.GetParent(destinationFolderPath));
        var movedFolder = FindFolderItem(destinationFolderPath);
        if (movedFolder is not null)
        {
            movedFolder.IsExpanded = sourceItem.IsExpanded;
        }
        SelectExplorerItem(FindFolderItem(destinationFolderPath));
    }

    private static void CancelInlineRenameRecursive(SessionExplorerItemViewModel item)
    {
        item.IsEditingName = false;
        item.EditingName = item.DisplayName;

        foreach (var child in item.Children)
        {
            CancelInlineRenameRecursive(child);
        }
    }

    private async Task RefreshSessionsAsync()
    {
        await RefreshSessionsAsync(CancellationToken.None);
    }

    private async Task RefreshSessionsAsync(CancellationToken cancellationToken)
    {
        var workspace = await _sessionRepository.LoadWorkspaceAsync(cancellationToken);

        _profiles.Clear();
        foreach (var profile in workspace.Profiles)
        {
            profile.GroupPath = SessionFolderPath.Normalize(profile.GroupPath);
            _profiles.Add(profile);
        }

        _folderPaths.Clear();
        foreach (var folderPath in workspace.FolderPaths.Select(SessionFolderPath.Normalize).Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            _folderPaths.Add(folderPath);
        }

        _terminalDefaults = NormalizeTerminalDefaults(workspace.TerminalDefaults);
        foreach (var profile in _profiles)
        {
            NormalizeProfileTerminalAppearance(profile);
        }

        BuildSessionTree();
    }

    private async Task CreateSessionAsync()
    {
        var result = await _dialogService.ShowSessionEditorAsync(new SessionEditorRequest
        {
            Title = "新建会话",
            Profile = CreateDefaultProfile(GetSelectionFolderPath()),
            HasStoredSecret = false,
            BrowseRemoteDirectoryAsync = BrowseRemoteDirectoryAsync,
        });

        if (result is null)
        {
            return;
        }

        result.Profile.GroupPath = SessionFolderPath.Normalize(result.Profile.GroupPath);
        _profiles.Add(result.Profile);
        await ApplyEditedSecretAsync(result, CancellationToken.None);
        await PersistWorkspaceAsync(CancellationToken.None);
        BuildSessionTree();
        SelectExplorerItem(FindSessionItem(result.Profile.Id));
    }

    private async Task EditSelectedSessionAsync()
    {
        var selectedSession = SelectedSession;
        if (selectedSession is null)
        {
            return;
        }

        var sessionId = selectedSession.Id;
        var hasStoredSecret = !string.IsNullOrEmpty(await _credentialStore.GetPasswordAsync(sessionId));
        var result = await _dialogService.ShowSessionEditorAsync(new SessionEditorRequest
        {
            Title = "编辑会话",
            Profile = CloneProfile(selectedSession),
            HasStoredSecret = hasStoredSecret,
            BrowseRemoteDirectoryAsync = BrowseRemoteDirectoryAsync,
        });

        if (result is null)
        {
            return;
        }

        result.Profile.GroupPath = SessionFolderPath.Normalize(result.Profile.GroupPath);
        CopyProfileValues(result.Profile, selectedSession);
        InvalidateRuntimeSecret(sessionId);
        await ApplyEditedSecretAsync(result, CancellationToken.None);
        await PersistWorkspaceAsync(CancellationToken.None);

        foreach (var tab in Tabs.Where(tab => tab.Profile.Id == sessionId))
        {
            tab.RefreshHeader();
            tab.NotifyAppearanceChanged();
        }

        BuildSessionTree();
        SelectExplorerItem(FindSessionItem(sessionId));
    }

    private Task RenameSelectedSessionAsync()
    {
        BeginInlineRename(SelectedExplorerItem);
        return Task.CompletedTask;
    }

    public void CancelInlineRename(SessionExplorerItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        item.IsEditingName = false;
        item.EditingName = item.DisplayName;
    }

    public async Task CommitInlineRenameAsync(SessionExplorerItemViewModel? item, string? rawName)
    {
        if (item is null || !item.IsEditingName)
        {
            return;
        }

        item.IsEditingName = false;
        var newName = (rawName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(newName) ||
            string.Equals(item.DisplayName, newName, StringComparison.Ordinal))
        {
            item.EditingName = item.DisplayName;
            return;
        }

        if (item.Profile is not null)
        {
            await RenameSessionAsync(item.Profile, newName);
            return;
        }

        if (item.CanManageFolder)
        {
            await RenameFolderAsync(item, newName);
        }
    }

    private async Task DuplicateSelectedSessionAsync()
    {
        if (SelectedSession is null)
        {
            return;
        }

        var result = await _dialogService.ShowSessionEditorAsync(new SessionEditorRequest
        {
            Title = "复制会话",
            Profile = SelectedSession.CreateDuplicate(),
            HasStoredSecret = false,
            BrowseRemoteDirectoryAsync = BrowseRemoteDirectoryAsync,
        });

        if (result is null)
        {
            return;
        }

        result.Profile.GroupPath = SessionFolderPath.Normalize(result.Profile.GroupPath);
        _profiles.Add(result.Profile);
        await PersistWorkspaceAsync(CancellationToken.None);
        BuildSessionTree();
        SelectExplorerItem(FindSessionItem(result.Profile.Id));
    }

    private async Task DeleteSelectedSessionAsync()
    {
        if (SelectedSession is null)
        {
            return;
        }

        var confirmed = await _dialogService.ConfirmDeleteAsync("删除会话", $"确定要删除会话“{SelectedSession.Name}”吗？");
        if (!confirmed)
        {
            return;
        }

        await _credentialStore.DeletePasswordAsync(SelectedSession.Id);
        InvalidateRuntimeSecret(SelectedSession.Id);
        _profiles.Remove(SelectedSession);
        await _sessionRepository.SaveWorkspaceAsync(BuildWorkspaceSnapshot());
        BuildSessionTree();
    }

    private async Task EditTerminalDefaultsAsync()
    {
        var oldDefaults = _terminalDefaults.CreateCopy();
        var result = await _dialogService.ShowTerminalDefaultsAsync(new TerminalDefaultsRequest
        {
            Title = "终端字体设置",
            FontSize = oldDefaults.FontSize,
            FontFamily = oldDefaults.FontFamily,
            ApplyToMatchingSessions = true,
        });

        if (result is null)
        {
            return;
        }

        var newDefaults = NormalizeTerminalDefaults(new TerminalDefaultsSettings
        {
            FontSize = result.FontSize,
            FontFamily = result.FontFamily,
        });

        _terminalDefaults = newDefaults;

        if (result.ApplyToMatchingSessions)
        {
            ApplyTerminalDefaultsToMatchingSessions(oldDefaults, newDefaults);
        }

        await PersistWorkspaceAsync(CancellationToken.None);
    }

    private async Task ConnectSelectedSessionAsync()
    {
        if (SelectedSession is null)
        {
            return;
        }

        await OpenSessionTabAsync(SelectedSession);
    }

    private async Task DisconnectSelectedTabAsync()
    {
        if (SelectedTab is null)
        {
            return;
        }

        await SelectedTab.CloseAsync();
    }

    private async Task ReconnectSelectedTabAsync()
    {
        var selectedTab = SelectedTab;
        if (selectedTab is null)
        {
            return;
        }

        try
        {
            await selectedTab.ReconnectAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await ForgetSecretAfterAuthenticationFailureAsync(selectedTab.Profile, ex, CancellationToken.None);
            await _dialogService.ShowErrorAsync("重新连接失败", ex.Message);
        }
    }

    private async Task RenameSelectedTabAsync()
    {
        if (SelectedTab is null)
        {
            return;
        }

        var newName = await _dialogService.ShowTextPromptAsync("重命名标签", "请输入新的标签名称：", SelectedTab.Header);
        if (string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        SelectedTab.OverrideHeader(newName);
    }

    private async Task OpenSelectedSftpAsync()
    {
        var session = ActiveSession;
        if (session is null)
        {
            return;
        }

        try
        {
            var request = await BuildSftpConnectionRequestAsync(session, CancellationToken.None);
            await _openSftpBrowserAsync(request);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await ForgetSecretAfterAuthenticationFailureAsync(session, ex, CancellationToken.None);
            await _dialogService.ShowErrorAsync("SFTP 打开失败", ex.Message);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task CreateFolderAsync()
    {
        var parentFolderPath = GetSelectionFolderPath();
        var folderName = await _dialogService.ShowTextPromptAsync("新建文件夹", "请输入文件夹名称：");
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return;
        }

        var targetFolderPath = SessionFolderPath.Combine(parentFolderPath, folderName);
        if (string.IsNullOrWhiteSpace(targetFolderPath))
        {
            return;
        }

        if (GetAllFolderPaths().Contains(targetFolderPath, StringComparer.OrdinalIgnoreCase))
        {
            await _dialogService.ShowInfoAsync("新建文件夹", $"文件夹“{targetFolderPath}”已存在。");
            return;
        }

        _folderPaths.Add(targetFolderPath);
        await _sessionRepository.SaveWorkspaceAsync(BuildWorkspaceSnapshot());
        BuildSessionTree();
        SelectExplorerItem(FindFolderItem(targetFolderPath));
    }

    private Task RenameSelectedFolderAsync()
    {
        BeginInlineRename(SelectedExplorerItem);
        return Task.CompletedTask;
    }

    private async Task RenameSessionAsync(SessionProfile selectedSession, string newName)
    {
        selectedSession.Name = newName;
        await PersistWorkspaceAsync(CancellationToken.None);

        foreach (var tab in Tabs.Where(tab => tab.Profile.Id == selectedSession.Id))
        {
            tab.RefreshHeader();
        }

        BuildSessionTree();
        SelectExplorerItem(FindSessionItem(selectedSession.Id));
    }

    private async Task RenameFolderAsync(SessionExplorerItemViewModel folder, string newName)
    {
        var oldFolderPath = folder.FolderPath;
        var newFolderPath = SessionFolderPath.Combine(SessionFolderPath.GetParent(oldFolderPath), newName);
        if (oldFolderPath.Equals(newFolderPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (GetAllFolderPaths().Contains(newFolderPath, StringComparer.OrdinalIgnoreCase))
        {
            await _dialogService.ShowInfoAsync("重命名文件夹", $"文件夹“{newFolderPath}”已存在。");
            return;
        }

        ReplaceFolderPrefix(oldFolderPath, newFolderPath);
        await PersistWorkspaceAsync(CancellationToken.None);
        BuildSessionTree();
        SelectExplorerItem(FindFolderItem(newFolderPath));
    }

    private async Task DeleteSelectedFolderAsync()
    {
        var folder = SelectedExplorerItem;
        if (folder?.CanManageFolder != true)
        {
            return;
        }

        var folderPath = folder.FolderPath;
        var parentFolderPath = SessionFolderPath.GetParent(folderPath);
        var containsSessions = _profiles.Any(profile => SessionFolderPath.IsSameOrDescendant(profile.GroupPath, folderPath));
        var containsSubFolders = GetAllFolderPaths()
            .Any(path => !path.Equals(folderPath, StringComparison.OrdinalIgnoreCase) &&
                         SessionFolderPath.IsSameOrDescendant(path, folderPath));

        var message = containsSessions || containsSubFolders
            ? $"删除文件夹“{folder.DisplayName}”后，其中的会话和子文件夹会移动到上一级。是否继续？"
            : $"确定要删除文件夹“{folder.DisplayName}”吗？";

        var confirmed = await _dialogService.ConfirmDeleteAsync("删除文件夹", message);
        if (!confirmed)
        {
            return;
        }

        ReplaceFolderPrefix(folderPath, parentFolderPath, removeSourceFolder: true);
        await PersistWorkspaceAsync(CancellationToken.None);
        BuildSessionTree();
        SelectExplorerItem(string.IsNullOrEmpty(parentFolderPath) ? FindRootSessionsItem() : FindFolderItem(parentFolderPath));
    }

    private async Task OpenSessionTabAsync(SessionProfile profile)
    {
        var tab = new TerminalTabViewModel(
            profile,
            _dispatcher,
            _terminalSessionFactory,
            cancellationToken => BuildTerminalRequestAsync(profile, cancellationToken),
            CloseTabAsync,
            request => StoreRuntimeSecret(profile, request.Secret));

        Tabs.Add(tab);
        SelectedTab = tab;

        try
        {
            await tab.StartAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await ForgetSecretAfterAuthenticationFailureAsync(profile, ex, CancellationToken.None);
            await _dialogService.ShowErrorAsync("连接失败", ex.Message);
        }
    }

    private async Task CloseTabAsync(TerminalTabViewModel tab)
    {
        await tab.DisposeAsync();
        Tabs.Remove(tab);
        if (ReferenceEquals(SelectedTab, tab))
        {
            SelectedTab = Tabs.LastOrDefault();
        }
    }

    private async Task<TerminalSessionStartRequest> BuildTerminalRequestAsync(SessionProfile profile, CancellationToken cancellationToken)
    {
        var secret = await ResolveSecretAsync(profile, cancellationToken);
        return new TerminalSessionStartRequest
        {
            Profile = profile,
            Secret = secret,
            HostKeyVerificationCallback = (request, innerCt) => _dialogService.ConfirmHostKeyAsync(request, innerCt),
        };
    }

    private async Task<SftpConnectionRequest> BuildSftpConnectionRequestAsync(SessionProfile profile, CancellationToken cancellationToken)
    {
        if (TryCreateLiveSftpConnectionRequest(profile, out var liveRequest))
        {
            return liveRequest;
        }

        var secret = await ResolveSecretAsync(profile, cancellationToken);
        return CreateSftpConnectionRequest(profile, secret);
    }

    private bool TryCreateLiveSftpConnectionRequest(SessionProfile profile, out SftpConnectionRequest request)
    {
        var liveTab = FindConnectedTabForProfile(profile);
        if (liveTab is not null && liveTab.TryCreateSftpConnectionRequest(out var liveRequest) && liveRequest is not null)
        {
            request = liveRequest;
            return true;
        }

        request = null!;
        return false;
    }

    private TerminalTabViewModel? FindConnectedTabForProfile(SessionProfile profile)
    {
        if (SelectedTab is not null &&
            SelectedTab.IsConnected &&
            SelectedTab.Profile.Id == profile.Id)
        {
            return SelectedTab;
        }

        return Tabs.LastOrDefault(tab => tab.IsConnected && tab.Profile.Id == profile.Id);
    }

    private SftpConnectionRequest CreateSftpConnectionRequest(SessionProfile profile, string? secret)
    {
        return new SftpConnectionRequest
        {
            Profile = CloneProfile(profile),
            Secret = secret,
            HostKeyVerificationCallback = (request, innerCt) => _dialogService.ConfirmHostKeyAsync(request, innerCt),
        };
    }

    private async Task<string?> ResolveSecretAsync(SessionProfile profile, CancellationToken cancellationToken)
    {
        if (TryGetRuntimeSecret(profile, out var runtimeSecret))
        {
            return runtimeSecret;
        }

        if (profile.RememberSecret)
        {
            var remembered = await _credentialStore.GetPasswordAsync(profile.Id, cancellationToken);
            if (!string.IsNullOrEmpty(remembered))
            {
                return remembered;
            }
        }

        var promptResult = await _dialogService.ShowSecretPromptAsync(
            new SecretPromptRequest
            {
                Title = profile.AuthType == AuthenticationType.Password ? "输入密码" : "输入私钥口令",
                Message = profile.AuthType == AuthenticationType.Password
                    ? $"请输入 {profile.Name} 的 SSH 登录密码。"
                    : "如果私钥没有口令，可以直接留空并继续。",
                Profile = CloneProfile(profile),
                AllowEmpty = profile.AuthType == AuthenticationType.PrivateKey,
                RememberSecret = profile.RememberSecret,
            },
            cancellationToken);

        if (promptResult is null)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (profile.AuthType == AuthenticationType.Password && string.IsNullOrWhiteSpace(promptResult.Secret))
        {
            throw new InvalidOperationException("密码不能为空。");
        }

        if (profile.RememberSecret != promptResult.RememberSecret)
        {
            profile.RememberSecret = promptResult.RememberSecret;
            await PersistWorkspaceAsync(CancellationToken.None);
        }

        if (!profile.RememberSecret)
        {
            await _credentialStore.DeletePasswordAsync(profile.Id, CancellationToken.None);
        }
        else if (!string.IsNullOrEmpty(promptResult.Secret))
        {
            await _credentialStore.SavePasswordAsync(profile.Id, promptResult.Secret, CancellationToken.None);
        }
        else
        {
            await _credentialStore.DeletePasswordAsync(profile.Id, CancellationToken.None);
        }

        return string.IsNullOrWhiteSpace(promptResult.Secret) ? null : promptResult.Secret;
    }

    private async Task<string?> BrowseRemoteDirectoryAsync(SessionProfile profile, CancellationToken cancellationToken)
    {
        var normalizedProfile = CloneProfile(profile);
        var request = await BuildSftpConnectionRequestAsync(normalizedProfile, cancellationToken);
        var viewModel = new RemoteDirectoryPickerViewModel(
            _dispatcher,
            request,
            _sftpSessionFactory,
            _dialogService);

        var window = new Dialogs.RemoteDirectoryPickerWindow(viewModel)
        {
            Owner = Application.Current.MainWindow,
        };

        try
        {
            await window.InitializeAsync(cancellationToken);
            return window.ShowDialog() == true ? window.SelectedPath : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await ForgetSecretAfterAuthenticationFailureAsync(profile, ex, cancellationToken);
            throw;
        }
        finally
        {
            await viewModel.DisposeAsync();
        }
    }

    private bool TryGetRuntimeSecret(SessionProfile profile, out string? secret)
    {
        if (_sessionSecretCache.TryGetSecret(profile.Id, out secret))
        {
            if (profile.AuthType == AuthenticationType.PrivateKey)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(secret);
        }

        secret = null;
        return false;
    }

    private void StoreRuntimeSecret(SessionProfile profile, string? secret)
    {
        if (profile.AuthType == AuthenticationType.Password && string.IsNullOrWhiteSpace(secret))
        {
            return;
        }

        _sessionSecretCache.StoreSecret(profile.Id, string.IsNullOrWhiteSpace(secret) ? null : secret);
    }

    private async Task ApplyEditedSecretAsync(SessionEditorResult result, CancellationToken cancellationToken)
    {
        var profile = result.Profile;
        if (!profile.RememberSecret)
        {
            await _credentialStore.DeletePasswordAsync(profile.Id, cancellationToken);
            if (result.SecretWasEdited && !string.IsNullOrWhiteSpace(result.Secret))
            {
                StoreRuntimeSecret(profile, result.Secret);
            }

            return;
        }

        if (!result.SecretWasEdited || string.IsNullOrWhiteSpace(result.Secret))
        {
            return;
        }

        await _credentialStore.SavePasswordAsync(profile.Id, result.Secret, cancellationToken);
        StoreRuntimeSecret(profile, result.Secret);
    }

    private void InvalidateRuntimeSecret(Guid sessionId)
    {
        _sessionSecretCache.RemoveSecret(sessionId);
    }

    private async Task ForgetSecretAfterAuthenticationFailureAsync(
        SessionProfile profile,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (profile.AuthType != AuthenticationType.Password || !IsAuthenticationFailure(exception))
        {
            return;
        }

        InvalidateRuntimeSecret(profile.Id);
        await _credentialStore.DeletePasswordAsync(profile.Id, cancellationToken);
    }

    private static bool IsAuthenticationFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.GetType().Name.Contains("Authentication", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task PersistWorkspaceAsync(CancellationToken cancellationToken)
    {
        await _sessionRepository.SaveWorkspaceAsync(BuildWorkspaceSnapshot(), cancellationToken);
    }

    private SessionWorkspace BuildWorkspaceSnapshot()
    {
        return new SessionWorkspace
        {
            Profiles = _profiles
                .Select(CloneProfile)
                .ToList(),
            FolderPaths = _folderPaths
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            TerminalDefaults = _terminalDefaults.CreateCopy(),
        };
    }

    private void BuildSessionTree()
    {
        var expansionStates = CaptureExpansionStates();
        SessionTree.Clear();

        var favoriteProfiles = _profiles
            .Where(profile => profile.IsFavorite)
            .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (favoriteProfiles.Count > 0)
        {
            var favoritesRoot = new SessionExplorerItemViewModel("收藏夹", folderPath: string.Empty, isVirtualRoot: true);
            foreach (var favorite in favoriteProfiles)
            {
                favoritesRoot.Children.Add(new SessionExplorerItemViewModel(favorite.Name, favorite, favorite.GroupPath));
            }

            SessionTree.Add(favoritesRoot);
        }

        var sessionsRoot = new SessionExplorerItemViewModel("所有会话", folderPath: string.Empty, isVirtualRoot: true);

        foreach (var folderPath in GetAllFolderPaths()
                     .OrderBy(path => path.Count(ch => ch == '/'))
                     .ThenBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            AddFolderToTree(sessionsRoot, folderPath);
        }

        foreach (var profile in _profiles.OrderBy(profile => profile.GroupPath, StringComparer.OrdinalIgnoreCase).ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase))
        {
            AddProfileToTree(sessionsRoot, profile);
        }

        SessionTree.Add(sessionsRoot);
        ApplyExpansionStates(expansionStates);
        NotifySelectionPropertiesChanged();
    }

    private void AddFolderToTree(SessionExplorerItemViewModel root, string folderPath)
    {
        var current = root;
        var currentPath = string.Empty;

        foreach (var segment in folderPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            currentPath = SessionFolderPath.Combine(currentPath, segment);
            var existing = current.Children.FirstOrDefault(
                child => child.IsFolder &&
                         child.FolderPath.Equals(currentPath, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                existing = new SessionExplorerItemViewModel(segment, folderPath: currentPath);
                current.Children.Add(existing);
                SortTreeChildren(current);
            }

            current = existing;
        }
    }

    private void AddProfileToTree(SessionExplorerItemViewModel root, SessionProfile profile)
    {
        var current = root;
        var normalizedGroupPath = SessionFolderPath.Normalize(profile.GroupPath);

        if (!string.IsNullOrEmpty(normalizedGroupPath))
        {
            AddFolderToTree(root, normalizedGroupPath);
            current = FindFolderItem(normalizedGroupPath, root) ?? root;
        }

        current.Children.Add(new SessionExplorerItemViewModel(profile.Name, profile, normalizedGroupPath));
        SortTreeChildren(current);
    }

    private IReadOnlyList<string> GetAllFolderPaths()
    {
        var folderPaths = new HashSet<string>(_folderPaths, StringComparer.OrdinalIgnoreCase);

        foreach (var profileFolder in _profiles
                     .Select(profile => SessionFolderPath.Normalize(profile.GroupPath))
                     .Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var current = string.Empty;
            foreach (var segment in profileFolder.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                current = SessionFolderPath.Combine(current, segment);
                folderPaths.Add(current);
            }
        }

        return folderPaths.ToList();
    }

    private void ReplaceFolderPrefix(string oldFolderPath, string newFolderPath, bool removeSourceFolder = false)
    {
        var nextFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folderPath in _folderPaths)
        {
            if (folderPath.Equals(oldFolderPath, StringComparison.OrdinalIgnoreCase))
            {
                if (!removeSourceFolder && !string.IsNullOrWhiteSpace(newFolderPath))
                {
                    nextFolders.Add(newFolderPath);
                }

                continue;
            }

            if (SessionFolderPath.IsSameOrDescendant(folderPath, oldFolderPath))
            {
                var updatedPath = SessionFolderPath.ReplacePrefix(folderPath, oldFolderPath, newFolderPath);
                if (!string.IsNullOrWhiteSpace(updatedPath))
                {
                    nextFolders.Add(updatedPath);
                }

                continue;
            }

            nextFolders.Add(folderPath);
        }

        _folderPaths.Clear();
        foreach (var folderPath in nextFolders)
        {
            _folderPaths.Add(folderPath);
        }

        foreach (var profile in _profiles.Where(profile => SessionFolderPath.IsSameOrDescendant(profile.GroupPath, oldFolderPath)))
        {
            profile.GroupPath = SessionFolderPath.ReplacePrefix(profile.GroupPath, oldFolderPath, newFolderPath);
        }
    }

    private string GetSelectionFolderPath()
    {
        if (SelectedExplorerItem is null)
        {
            return string.Empty;
        }

        if (SelectedExplorerItem.IsFolder)
        {
            return SelectedExplorerItem.IsVirtualRoot ? string.Empty : SelectedExplorerItem.FolderPath;
        }

        return SessionFolderPath.Normalize(SelectedSession?.GroupPath);
    }

    private string? ResolveDropTargetFolderPath(SessionExplorerItemViewModel? targetItem)
    {
        if (targetItem is null)
        {
            return string.Empty;
        }

        if (IsFavoritesRootItem(targetItem))
        {
            return null;
        }

        if (targetItem.IsVirtualRoot)
        {
            return string.Empty;
        }

        if (targetItem.IsFolder)
        {
            return targetItem.FolderPath;
        }

        return null;
    }

    private SessionExplorerItemViewModel? FindSessionItem(Guid profileId)
    {
        var sessionsRoot = FindRootSessionsItem();
        var found = sessionsRoot is null ? null : FindSessionItem(profileId, sessionsRoot);
        if (found is not null)
        {
            return found;
        }

        return SessionTree
            .Where(root => !ReferenceEquals(root, sessionsRoot))
            .Select(root => FindSessionItem(profileId, root))
            .FirstOrDefault(candidate => candidate is not null);
    }

    private SessionExplorerItemViewModel? FindSessionItem(Guid profileId, SessionExplorerItemViewModel node)
    {
        if (node.Profile?.Id == profileId)
        {
            return node;
        }

        foreach (var child in node.Children)
        {
            var found = FindSessionItem(profileId, child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private SessionExplorerItemViewModel? FindFolderItem(string folderPath)
    {
        return FindFolderItem(folderPath, FindRootSessionsItem());
    }

    private SessionExplorerItemViewModel? FindFolderItem(string folderPath, SessionExplorerItemViewModel? root)
    {
        if (root is null)
        {
            return null;
        }

        if (root.IsFolder && root.FolderPath.Equals(folderPath, StringComparison.OrdinalIgnoreCase))
        {
            return root;
        }

        foreach (var child in root.Children)
        {
            var found = FindFolderItem(folderPath, child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private SessionExplorerItemViewModel? FindRootSessionsItem()
    {
        return SessionTree.LastOrDefault(item => item.IsVirtualRoot);
    }

    private Dictionary<string, bool> CaptureExpansionStates()
    {
        var expansionStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in SessionTree)
        {
            CaptureExpansionStates(root, expansionStates);
        }

        return expansionStates;
    }

    private void CaptureExpansionStates(
        SessionExplorerItemViewModel node,
        IDictionary<string, bool> expansionStates)
    {
        if (TryGetExpansionStateKey(node, out var key))
        {
            expansionStates[key] = node.IsExpanded;
        }

        foreach (var child in node.Children)
        {
            CaptureExpansionStates(child, expansionStates);
        }
    }

    private void ApplyExpansionStates(IReadOnlyDictionary<string, bool> expansionStates)
    {
        foreach (var root in SessionTree)
        {
            ApplyExpansionStates(root, expansionStates);
        }
    }

    private void ApplyExpansionStates(
        SessionExplorerItemViewModel node,
        IReadOnlyDictionary<string, bool> expansionStates)
    {
        if (TryGetExpansionStateKey(node, out var key))
        {
            node.IsExpanded = expansionStates.TryGetValue(key, out var isExpanded)
                ? isExpanded
                : node.IsVirtualRoot;
        }

        foreach (var child in node.Children)
        {
            ApplyExpansionStates(child, expansionStates);
        }
    }

    private void ExpandFolderPath(string? folderPath)
    {
        var sessionsRoot = FindRootSessionsItem();
        if (sessionsRoot is null)
        {
            return;
        }

        sessionsRoot.IsExpanded = true;
        var normalizedFolderPath = SessionFolderPath.Normalize(folderPath);
        if (string.IsNullOrWhiteSpace(normalizedFolderPath))
        {
            return;
        }

        var currentPath = string.Empty;
        foreach (var segment in normalizedFolderPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            currentPath = SessionFolderPath.Combine(currentPath, segment);
            var folder = FindFolderItem(currentPath, sessionsRoot);
            if (folder is null)
            {
                break;
            }

            folder.IsExpanded = true;
        }
    }

    private static bool TryGetExpansionStateKey(SessionExplorerItemViewModel node, out string key)
    {
        if (node.IsVirtualRoot)
        {
            key = $"root:{node.DisplayName}";
            return true;
        }

        if (node.IsFolder)
        {
            key = $"folder:{node.FolderPath}";
            return true;
        }

        key = string.Empty;
        return false;
    }

    private static void SortTreeChildren(SessionExplorerItemViewModel folder)
    {
        var ordered = folder.Children
            .OrderByDescending(item => item.IsFolder)
            .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        folder.Children.Clear();
        foreach (var item in ordered)
        {
            folder.Children.Add(item);
        }
    }

    private void NotifySelectionPropertiesChanged()
    {
        OnPropertyChanged(nameof(SelectedSession));
        OnPropertyChanged(nameof(ActiveSession));
        OnPropertyChanged(nameof(SelectedItemNameText));
        OnPropertyChanged(nameof(SelectedItemTypeText));
        OnPropertyChanged(nameof(SelectedItemEndpointText));
        OnPropertyChanged(nameof(SelectedItemUserText));
        OnPropertyChanged(nameof(SelectedItemGroupText));
        OnPropertyChanged(nameof(SelectedItemAuthenticationText));
        OnPropertyChanged(nameof(SelectedItemChildrenText));
        OnPropertyChanged(nameof(StatusBarSessionText));
    }

    private void NotifyCommandStates()
    {
        EditSessionCommand.NotifyCanExecuteChanged();
        RenameSessionCommand.NotifyCanExecuteChanged();
        DuplicateSessionCommand.NotifyCanExecuteChanged();
        DeleteSessionCommand.NotifyCanExecuteChanged();
        ConnectSessionCommand.NotifyCanExecuteChanged();
        DisconnectSessionCommand.NotifyCanExecuteChanged();
        ReconnectSessionCommand.NotifyCanExecuteChanged();
        RenameTabCommand.NotifyCanExecuteChanged();
        OpenSftpCommand.NotifyCanExecuteChanged();
        RenameFolderCommand.NotifyCanExecuteChanged();
        DeleteFolderCommand.NotifyCanExecuteChanged();
        EditTerminalDefaultsCommand.NotifyCanExecuteChanged();
    }

    private SessionProfile CreateDefaultProfile(string groupPath)
    {
        return new SessionProfile
        {
            GroupPath = SessionFolderPath.Normalize(groupPath),
            FontSize = _terminalDefaults.FontSize,
            TerminalFontFamily = _terminalDefaults.FontFamily,
        };
    }

    private void ApplyTerminalDefaultsToMatchingSessions(TerminalDefaultsSettings oldDefaults, TerminalDefaultsSettings newDefaults)
    {
        foreach (var profile in _profiles)
        {
            var changed = false;
            if (Math.Abs(profile.FontSize - oldDefaults.FontSize) < 0.001d)
            {
                profile.FontSize = newDefaults.FontSize;
                changed = true;
            }

            if (string.Equals(
                    TerminalFontCatalog.NormalizeFontFamily(profile.TerminalFontFamily),
                    TerminalFontCatalog.NormalizeFontFamily(oldDefaults.FontFamily),
                    StringComparison.OrdinalIgnoreCase))
            {
                profile.TerminalFontFamily = newDefaults.FontFamily;
                changed = true;
            }

            if (!changed)
            {
                continue;
            }

            foreach (var tab in Tabs.Where(tab => tab.Profile.Id == profile.Id))
            {
                tab.NotifyAppearanceChanged();
            }
        }
    }

    private void NormalizeProfileTerminalAppearance(SessionProfile profile)
    {
        if (profile.FontSize < 9d)
        {
            profile.FontSize = _terminalDefaults.FontSize;
        }

        profile.TerminalFontFamily = TerminalFontCatalog.NormalizeFontFamily(profile.TerminalFontFamily);
    }

    private static TerminalDefaultsSettings NormalizeTerminalDefaults(TerminalDefaultsSettings terminalDefaults)
    {
        var normalized = terminalDefaults?.CreateCopy() ?? new TerminalDefaultsSettings();
        if (normalized.FontSize < 9d)
        {
            normalized.FontSize = TerminalDefaultsSettings.DefaultFontSize;
        }

        normalized.FontFamily = TerminalFontCatalog.NormalizeFontFamily(normalized.FontFamily);
        return normalized;
    }

    private static SessionProfile CloneProfile(SessionProfile profile)
    {
        return new SessionProfile
        {
            Id = profile.Id,
            Name = profile.Name,
            GroupPath = SessionFolderPath.Normalize(profile.GroupPath),
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

    private static void CopyProfileValues(SessionProfile source, SessionProfile target)
    {
        target.Name = source.Name;
        target.GroupPath = SessionFolderPath.Normalize(source.GroupPath);
        target.Host = source.Host;
        target.Port = source.Port;
        target.Username = source.Username;
        target.AuthType = source.AuthType;
        target.PrivateKeyPath = source.PrivateKeyPath;
        target.TerminalTheme = source.TerminalTheme;
        target.LocalStartupDirectory = source.LocalStartupDirectory;
        target.StartupDirectory = source.StartupDirectory;
        target.KeepAliveSeconds = source.KeepAliveSeconds;
        target.FontSize = source.FontSize;
        target.TerminalFontFamily = source.TerminalFontFamily;
        target.ScrollbackLines = source.ScrollbackLines;
        target.IsFavorite = source.IsFavorite;
        target.RememberSecret = source.RememberSecret;
    }
}
