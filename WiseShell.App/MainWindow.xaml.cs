using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WiseShell.App.ViewModels;

namespace WiseShell.App;

public partial class MainWindow : Window
{
    private Point? _dragStartPoint;
    private SessionExplorerItemViewModel? _dragSourceItem;

    public MainWindow()
    {
        InitializeComponent();
    }

    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext;

    private async void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "初始化失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SessionTreeView_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        ViewModel.SelectExplorerItem(e.NewValue as SessionExplorerItemViewModel);
    }

    private async void SessionTreeView_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<TextBox>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (SessionTreeView.SelectedItem is SessionExplorerItemViewModel { Profile: not null })
        {
            await ViewModel.ConnectSessionCommand.ExecuteAsync();
        }
    }

    private void SessionTreeView_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (FindAncestor<TextBox>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (e.Key != Key.F2)
        {
            return;
        }

        var selectedItem = SessionTreeView.SelectedItem as SessionExplorerItemViewModel ?? ViewModel.SelectedExplorerItem;
        if (ViewModel.TryBeginInlineRename(selectedItem))
        {
            e.Handled = true;
        }
    }

    private void SessionRenameTextBox_OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
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

    private async void SessionRenameTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not SessionExplorerItemViewModel item)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                await ViewModel.CommitInlineRenameAsync(item, textBox.Text);
                break;
            case Key.Escape:
                e.Handled = true;
                ViewModel.CancelInlineRename(item);
                break;
        }
    }

    private async void SessionRenameTextBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: SessionExplorerItemViewModel { IsEditingName: true } item } textBox)
        {
            await ViewModel.CommitInlineRenameAsync(item, textBox.Text);
        }
    }

    private void SessionTreeView_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<TextBox>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        _dragStartPoint = e.GetPosition(SessionTreeView);
        _dragSourceItem = GetSessionItemFromSource(e.OriginalSource as DependencyObject);
    }

    private void SessionTreeView_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (FindAncestor<TextBox>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed ||
            _dragStartPoint is null ||
            _dragSourceItem is null)
        {
            return;
        }

        var currentPosition = e.GetPosition(SessionTreeView);
        if (Math.Abs(currentPosition.X - _dragStartPoint.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPosition.Y - _dragStartPoint.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (!ViewModel.CanStartDrag(_dragSourceItem))
        {
            ResetDragState();
            return;
        }

        var data = new DataObject(typeof(SessionExplorerItemViewModel), _dragSourceItem);
        DragDrop.DoDragDrop(SessionTreeView, data, DragDropEffects.Move);
        ResetDragState();
    }

    private void SessionTreeView_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var treeViewItem = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (treeViewItem is null)
        {
            return;
        }

        treeViewItem.IsSelected = true;
        treeViewItem.Focus();
    }

    private void SessionTreeView_OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var item = GetSessionItemFromSource(e.OriginalSource as DependencyObject) ?? ViewModel.SelectedExplorerItem;
        var menu = BuildSessionTreeContextMenu(item);
        SessionTreeView.ContextMenu = menu;
    }

    private void SessionTreeView_OnDragOver(object sender, DragEventArgs e)
    {
        var sourceItem = e.Data.GetData(typeof(SessionExplorerItemViewModel)) as SessionExplorerItemViewModel;
        var targetItem = GetSessionItemFromSource(e.OriginalSource as DependencyObject);

        e.Effects = ViewModel.CanDropExplorerItem(sourceItem, targetItem)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void SessionTreeView_OnDrop(object sender, DragEventArgs e)
    {
        var sourceItem = e.Data.GetData(typeof(SessionExplorerItemViewModel)) as SessionExplorerItemViewModel;
        var targetItem = GetSessionItemFromSource(e.OriginalSource as DependencyObject);

        if (!ViewModel.CanDropExplorerItem(sourceItem, targetItem))
        {
            e.Handled = true;
            return;
        }

        await ViewModel.MoveExplorerItemAsync(sourceItem, targetItem);
        e.Handled = true;
        ResetDragState();
    }

    private void Window_OnClosing(object? sender, CancelEventArgs e)
    {
        ViewModel.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private ContextMenu BuildSessionTreeContextMenu(SessionExplorerItemViewModel? item)
    {
        var menu = new ContextMenu();

        if (item?.Profile is not null)
        {
            menu.Items.Add(CreateMenuItem("连接", () => ViewModel.ConnectSessionCommand.ExecuteAsync()));
            menu.Items.Add(CreateMenuItem("打开 SFTP", () => ViewModel.OpenSftpCommand.ExecuteAsync()));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("编辑会话", () => ViewModel.EditSessionCommand.ExecuteAsync()));
            menu.Items.Add(CreateMenuItem("重命名会话", () => ViewModel.RenameSessionCommand.ExecuteAsync()));
            menu.Items.Add(CreateMenuItem("复制会话", () => ViewModel.DuplicateSessionCommand.ExecuteAsync()));
            menu.Items.Add(CreateMenuItem("删除会话", () => ViewModel.DeleteSessionCommand.ExecuteAsync()));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("新建文件夹", () => ViewModel.NewFolderCommand.ExecuteAsync()));
            return menu;
        }

        if (item is not null && !ViewModel.IsFavoritesRootItem(item))
        {
            menu.Items.Add(CreateMenuItem("新建会话", () => ViewModel.NewSessionCommand.ExecuteAsync()));
            menu.Items.Add(CreateMenuItem("新建文件夹", () => ViewModel.NewFolderCommand.ExecuteAsync()));

            if (item.CanManageFolder)
            {
                menu.Items.Add(new Separator());
                menu.Items.Add(CreateMenuItem("重命名文件夹", () => ViewModel.RenameFolderCommand.ExecuteAsync()));
                menu.Items.Add(CreateMenuItem("删除文件夹", () => ViewModel.DeleteFolderCommand.ExecuteAsync()));
            }

            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("刷新", () => ViewModel.RefreshSessionsCommand.ExecuteAsync()));
            return menu;
        }

        menu.Items.Add(CreateMenuItem("新建会话", () => ViewModel.NewSessionCommand.ExecuteAsync()));
        menu.Items.Add(CreateMenuItem("新建文件夹", () => ViewModel.NewFolderCommand.ExecuteAsync()));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("刷新", () => ViewModel.RefreshSessionsCommand.ExecuteAsync()));
        return menu;
    }

    private static MenuItem CreateMenuItem(string header, Func<Task> action)
    {
        var menuItem = new MenuItem
        {
            Header = header,
        };

        menuItem.Click += async (_, _) => await action();
        return menuItem;
    }

    private SessionExplorerItemViewModel? GetSessionItemFromSource(DependencyObject? source)
    {
        return FindAncestor<TreeViewItem>(source)?.DataContext as SessionExplorerItemViewModel;
    }

    private static T? FindAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T matched)
            {
                return matched;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void ResetDragState()
    {
        _dragStartPoint = null;
        _dragSourceItem = null;
    }
}
