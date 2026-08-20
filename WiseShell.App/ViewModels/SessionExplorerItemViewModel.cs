using System.Collections.ObjectModel;
using WiseShell.App.Infrastructure;
using WiseShell.Core.Infrastructure;
using WiseShell.Core.Models;

namespace WiseShell.App.ViewModels;

public sealed class SessionExplorerItemViewModel : ObservableObject
{
    private bool _isExpanded;
    private bool _isEditingName;
    private string _editingName = string.Empty;

    public SessionExplorerItemViewModel(
        string displayName,
        SessionProfile? profile = null,
        string? folderPath = null,
        bool isVirtualRoot = false)
    {
        DisplayName = displayName;
        Profile = profile;
        FolderPath = SessionFolderPath.Normalize(folderPath);
        IsVirtualRoot = isVirtualRoot;
    }

    public string DisplayName { get; }

    public SessionProfile? Profile { get; }

    public string FolderPath { get; }

    public bool IsVirtualRoot { get; }

    public bool IsFolder => Profile is null;

    public bool CanManageFolder => IsFolder && !IsVirtualRoot;

    public bool IsEditingName
    {
        get => _isEditingName;
        set => SetProperty(ref _isEditingName, value);
    }

    public string EditingName
    {
        get => _editingName;
        set => SetProperty(ref _editingName, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public ObservableCollection<SessionExplorerItemViewModel> Children { get; } = new();
}
