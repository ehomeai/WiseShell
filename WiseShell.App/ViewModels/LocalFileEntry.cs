using System.Globalization;
using System.IO;
using WiseShell.App.Infrastructure;

namespace WiseShell.App.ViewModels;

public sealed class LocalFileEntry : ObservableObject
{
    private bool _isEditingName;
    private string _editingName = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string FullPath { get; init; } = string.Empty;

    public bool IsDirectory { get; init; }

    public bool IsParentNavigation { get; init; }

    public long Size { get; init; }

    public DateTime LastWriteTime { get; init; }

    public string SizeDisplay => IsDirectory || IsParentNavigation ? string.Empty : Size.ToString("N0", CultureInfo.InvariantCulture);

    public string TypeLabel => IsParentNavigation
        ? "上级目录"
        : IsDirectory
            ? "文件夹"
            : string.IsNullOrWhiteSpace(Path.GetExtension(Name))
                ? "文件"
                : $"{Path.GetExtension(Name).TrimStart('.').ToUpperInvariant()} 文件";

    public string LastWriteDisplay => IsParentNavigation ? string.Empty : LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

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
}
