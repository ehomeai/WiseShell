using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace WiseShell.Core.Models;

public sealed class SftpEntry : INotifyPropertyChanged
{
    private bool _isEditingName;
    private string _editingName = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; init; } = string.Empty;

    public string FullPath { get; init; } = string.Empty;

    public bool IsDirectory { get; init; }

    public bool IsParentNavigation { get; init; }

    public long Size { get; init; }

    public DateTimeOffset LastWriteTimeUtc { get; init; }

    public string Permissions { get; init; } = string.Empty;

    public string SizeDisplay => IsDirectory || IsParentNavigation ? string.Empty : Size.ToString("N0", CultureInfo.InvariantCulture);

    public string LastWriteDisplay => IsParentNavigation ? string.Empty : LastWriteTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

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

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
