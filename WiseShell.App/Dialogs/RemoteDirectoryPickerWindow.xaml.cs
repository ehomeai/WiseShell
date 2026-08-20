using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WiseShell.App.ViewModels;
using WiseShell.Core.Models;

namespace WiseShell.App.Dialogs;

public partial class RemoteDirectoryPickerWindow : Window
{
    public RemoteDirectoryPickerWindow(RemoteDirectoryPickerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    public RemoteDirectoryPickerViewModel ViewModel => (RemoteDirectoryPickerViewModel)DataContext;

    public string SelectedPath { get; private set; } = string.Empty;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return ViewModel.InitializeAsync(cancellationToken);
    }

    private void Window_OnClosing(object? sender, CancelEventArgs e)
    {
        ViewModel.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private async void NavigateUp_OnClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.NavigateUpAsync();
    }

    private async void Refresh_OnClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshAsync();
    }

    private async void Go_OnClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.NavigateToInputAsync();
    }

    private async void PathTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await ViewModel.NavigateToInputAsync();
    }

    private async void EntriesGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.SelectedEntry is { IsDirectory: true } entry)
        {
            await ViewModel.NavigateIntoAsync(entry);
        }
    }

    private void SelectCurrent_OnClick(object sender, RoutedEventArgs e)
    {
        SelectedPath = ViewModel.GetSelectedPath();
        DialogResult = true;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
