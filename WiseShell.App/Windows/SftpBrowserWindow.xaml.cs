using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WiseShell.App.Services;
using WiseShell.App.ViewModels;
using WiseShell.Core.Models;

namespace WiseShell.App.Windows;

public partial class SftpBrowserWindow : Window
{
    private const string LocalEntryDragFormat = "WiseShell.LocalEntry";
    private const string RemoteEntryDragFormat = "WiseShell.RemoteEntry";

    private readonly IDialogService _dialogService = new DialogService();
    private Point _dragStartPoint;

    public SftpBrowserWindow()
    {
        InitializeComponent();
    }

    private SftpBrowserViewModel ViewModel => (SftpBrowserViewModel)DataContext;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return ViewModel.InitializeAsync(cancellationToken);
    }

    private void Window_OnClosing(object? sender, CancelEventArgs e)
    {
        ViewModel.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private async void Retry_OnClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.RetryAsync();
    }

    private async void LocalNavigateUp_OnClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.NavigateLocalUpAsync();
    }

    private async void LocalRefresh_OnClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshLocalAsync();
    }

    private async void Upload_OnClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.UploadSelectedLocalAsync();
    }

    private async void Download_OnClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.DownloadSelectedRemoteAsync();
    }

    private async void RemoteNavigateUp_OnClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.NavigateRemoteUpAsync();
    }

    private async void RemoteRefresh_OnClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshRemoteAsync();
    }

    private async void CreateFolder_OnClick(object sender, RoutedEventArgs e)
    {
        var folderName = await _dialogService.ShowTextPromptAsync("新建远程目录", "请输入远程目录名称：");
        if (!string.IsNullOrWhiteSpace(folderName))
        {
            await ViewModel.CreateDirectoryAsync(folderName);
        }
    }

    private async void Delete_OnClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.DeleteSelectedAsync();
    }

    private async void EntriesGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualParent<TextBox>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (ViewModel.SelectedEntry is { IsDirectory: true } entry)
        {
            await ViewModel.NavigateToAsync(entry);
        }
    }

    private async void LocalEntriesGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualParent<TextBox>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (ViewModel.SelectedLocalEntry is { IsDirectory: true } entry)
        {
            await ViewModel.NavigateLocalToAsync(entry);
        }
    }

    private async void LocalPathTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await ViewModel.NavigateLocalToInputAsync();
    }

    private async void LocalDriveComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || sender is not ComboBox { SelectedItem: string driveRoot })
        {
            return;
        }

        if (string.Equals(Path.GetPathRoot(ViewModel.CurrentLocalPath), driveRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await ViewModel.NavigateToLocalDriveAsync(driveRoot);
    }

    private async void RemotePathTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await ViewModel.NavigateRemoteToInputAsync();
    }

    private async void LocalUploadMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.UploadSelectedLocalAsync();
    }

    private async void LocalDeleteMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.DeleteSelectedLocalAsync();
    }

    private async void LocalRenameMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.RenameSelectedLocalAsync();
    }

    private async void RemoteDownloadMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.DownloadSelectedRemoteAsync();
    }

    private async void RemoteRenameMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.RenameSelectedAsync();
    }

    private async void LocalEntriesGrid_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Delete when ViewModel.CanDeleteLocalSelection:
                e.Handled = true;
                await ViewModel.DeleteSelectedLocalAsync();
                break;
            case Key.F2 when ViewModel.CanRenameLocalSelection:
                e.Handled = true;
                await ViewModel.RenameSelectedLocalAsync();
                break;
        }
    }

    private async void RemoteEntriesGrid_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Delete when ViewModel.CanDeleteSelection:
                e.Handled = true;
                await ViewModel.DeleteSelectedAsync();
                break;
            case Key.F2 when ViewModel.CanRenameSelection:
                e.Handled = true;
                await ViewModel.RenameSelectedAsync();
                break;
        }
    }

    private void TransferLogsGrid_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject) is { } row)
        {
            if (!row.IsSelected)
            {
                TransferLogsGrid.SelectedItems.Clear();
                row.IsSelected = true;
            }

            row.Focus();
            ViewModel.SetSelectedTransferLogs(TransferLogsGrid.SelectedItems.OfType<TransferLogEntry>());
        }
    }

    private void TransferLogsGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.SetSelectedTransferLogs(TransferLogsGrid.SelectedItems.OfType<TransferLogEntry>());
    }

    private void CancelSelectedTransfers_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.CancelSelectedTransfers();
    }

    private void CancelAllTransfers_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.CancelAllTransfers();
    }

    private void ClearTransferLogs_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearTransferLogs();
    }

    private void LocalEntriesGrid_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualParent<TextBox>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        _dragStartPoint = e.GetPosition(this);
    }

    private void RemoteEntriesGrid_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualParent<TextBox>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        _dragStartPoint = e.GetPosition(this);
    }

    private void FileRenameTextBox_OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox textBox || e.NewValue is not true)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            textBox.Focus();
            textBox.SelectAll();
        }));
    }

    private async void FileRenameTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                await CommitFileInlineRenameAsync(textBox);
                break;
            case Key.Escape:
                e.Handled = true;
                CancelFileInlineRename(textBox.DataContext);
                break;
        }
    }

    private async void FileRenameTextBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox && IsFileInlineRenameActive(textBox.DataContext))
        {
            await CommitFileInlineRenameAsync(textBox);
        }
    }

    private async Task CommitFileInlineRenameAsync(TextBox textBox)
    {
        switch (textBox.DataContext)
        {
            case LocalFileEntry localEntry:
                await ViewModel.CommitLocalInlineRenameAsync(localEntry, textBox.Text);
                break;
            case SftpEntry remoteEntry:
                await ViewModel.CommitRemoteInlineRenameAsync(remoteEntry, textBox.Text);
                break;
        }
    }

    private void CancelFileInlineRename(object? dataContext)
    {
        switch (dataContext)
        {
            case LocalFileEntry localEntry:
                ViewModel.CancelLocalInlineRename(localEntry);
                break;
            case SftpEntry remoteEntry:
                ViewModel.CancelRemoteInlineRename(remoteEntry);
                break;
        }
    }

    private static bool IsFileInlineRenameActive(object? dataContext)
    {
        return dataContext switch
        {
            LocalFileEntry { IsEditingName: true } => true,
            SftpEntry { IsEditingName: true } => true,
            _ => false,
        };
    }

    private void FileGrid_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid)
        {
            return;
        }

        if (FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject) is { } row)
        {
            if (!row.IsSelected)
            {
                grid.SelectedItem = row.Item;
            }

            row.Focus();
        }
    }

    private void LocalEntriesGrid_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (FindVisualParent<TextBox>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (!CanStartDrag(e))
        {
            return;
        }

        var entry = ViewModel.SelectedLocalEntry;
        if (entry is null || entry.IsParentNavigation || (!File.Exists(entry.FullPath) && !Directory.Exists(entry.FullPath)))
        {
            return;
        }

        var data = new DataObject();
        data.SetData(LocalEntryDragFormat, entry);
        data.SetData(DataFormats.FileDrop, new[] { entry.FullPath });
        DragDrop.DoDragDrop(LocalEntriesGrid, data, DragDropEffects.Copy);
    }

    private void RemoteEntriesGrid_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (FindVisualParent<TextBox>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (!CanStartDrag(e))
        {
            return;
        }

        var entry = ViewModel.SelectedEntry;
        if (entry is null || entry.IsParentNavigation)
        {
            return;
        }

        var data = new DataObject();
        data.SetData(RemoteEntryDragFormat, entry);
        DragDrop.DoDragDrop(RemoteEntriesGrid, data, DragDropEffects.Copy);
    }

    private void LocalEntriesGrid_OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = ViewModel.CanQueueTransfers && e.Data.GetDataPresent(RemoteEntryDragFormat)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void RemoteEntriesGrid_OnDragOver(object sender, DragEventArgs e)
    {
        var canAcceptLocalEntry = e.Data.GetDataPresent(LocalEntryDragFormat);
        var canAcceptExplorerDrop = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = ViewModel.CanQueueTransfers && (canAcceptLocalEntry || canAcceptExplorerDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void LocalEntriesGrid_OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;

        if (!ViewModel.CanQueueTransfers || !e.Data.GetDataPresent(RemoteEntryDragFormat))
        {
            return;
        }

        if (e.Data.GetData(RemoteEntryDragFormat) is not SftpEntry entry)
        {
            return;
        }

        var localPath = Path.Combine(ViewModel.CurrentLocalPath, entry.Name);
        await ViewModel.DownloadAsync(entry, localPath);
    }

    private async void RemoteEntriesGrid_OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;

        if (!ViewModel.CanQueueTransfers)
        {
            return;
        }

        if (e.Data.GetDataPresent(LocalEntryDragFormat))
        {
            if (e.Data.GetData(LocalEntryDragFormat) is LocalFileEntry entry)
            {
                if (entry.IsParentNavigation)
                {
                    return;
                }

                await ViewModel.UploadAsync(entry.FullPath);
            }

            return;
        }

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        var filePaths = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (filePaths is null || filePaths.Length == 0)
        {
            return;
        }

        foreach (var path in filePaths)
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                await ViewModel.UploadAsync(path);
            }
        }
    }

    private bool CanStartDrag(MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return false;
        }

        var currentPosition = e.GetPosition(this);
        return Math.Abs(currentPosition.X - _dragStartPoint.X) >= SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(currentPosition.Y - _dragStartPoint.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }

    private static T? FindVisualParent<T>(DependencyObject? dependencyObject)
        where T : DependencyObject
    {
        while (dependencyObject is not null)
        {
            if (dependencyObject is T match)
            {
                return match;
            }

            dependencyObject = VisualTreeHelper.GetParent(dependencyObject);
        }

        return null;
    }
}
