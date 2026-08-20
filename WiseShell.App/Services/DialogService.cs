using System.Windows;
using WiseShell.App.Dialogs;
using WiseShell.App.ViewModels;
using WiseShell.Core.Models;

namespace WiseShell.App.Services;

public sealed class DialogService : IDialogService
{
    public async Task<SessionEditorResult?> ShowSessionEditorAsync(SessionEditorRequest request, CancellationToken cancellationToken = default)
    {
        return await InvokeAsync(
            () =>
            {
                var window = new SessionEditWindow(
                    new SessionEditViewModel(request.Profile, request.HasStoredSecret),
                    request.BrowseRemoteDirectoryAsync)
                {
                    Title = request.Title,
                    Owner = GetOwner(),
                };

                if (window.ShowDialog() != true)
                {
                    return null;
                }

                return new SessionEditorResult
                {
                    Profile = window.ViewModel.ToProfile(),
                    Secret = window.Secret,
                    SecretWasEdited = window.SecretWasEdited,
                };
            },
            cancellationToken);
    }

    public async Task<SecretPromptResult?> ShowSecretPromptAsync(SecretPromptRequest request, CancellationToken cancellationToken = default)
    {
        return await InvokeAsync(
            () =>
            {
                var window = new SecretPromptWindow(request)
                {
                    Owner = GetOwner(),
                };

                if (window.ShowDialog() != true)
                {
                    return null;
                }

                return new SecretPromptResult
                {
                    Secret = window.Secret,
                    RememberSecret = window.RememberSecret,
                };
            },
            cancellationToken);
    }

    public async Task<TerminalDefaultsResult?> ShowTerminalDefaultsAsync(TerminalDefaultsRequest request, CancellationToken cancellationToken = default)
    {
        return await InvokeAsync(
            () =>
            {
                var window = new TerminalDefaultsWindow(new TerminalDefaultsViewModel(
                    request.FontSize,
                    request.FontFamily,
                    request.ApplyToMatchingSessions))
                {
                    Title = request.Title,
                    Owner = GetOwner(),
                };

                if (window.ShowDialog() != true)
                {
                    return null;
                }

                return new TerminalDefaultsResult
                {
                    FontSize = window.ViewModel.FontSize,
                    FontFamily = window.ViewModel.FontFamily,
                    ApplyToMatchingSessions = window.ViewModel.ApplyToMatchingSessions,
                };
            },
            cancellationToken);
    }

    public async Task<string?> ShowTextPromptAsync(string title, string message, string initialValue = "", CancellationToken cancellationToken = default)
    {
        return await InvokeAsync(
            () =>
            {
                var window = new TextPromptWindow(title, message, initialValue)
                {
                    Owner = GetOwner(),
                };

                return window.ShowDialog() == true ? window.Value : null;
            },
            cancellationToken);
    }

    public async Task<bool> ConfirmHostKeyAsync(HostKeyVerificationRequest request, CancellationToken cancellationToken = default)
    {
        return await InvokeAsync(
            () =>
            {
                var window = new HostKeyPromptWindow(request)
                {
                    Owner = GetOwner(),
                };

                return window.ShowDialog() == true;
            },
            cancellationToken);
    }

    public async Task<bool> ConfirmDeleteAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        return await InvokeAsync(
            () =>
            {
                var result = MessageBox.Show(GetOwner(), message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning);
                return result == MessageBoxResult.Yes;
            },
            cancellationToken);
    }

    public async Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        await InvokeAsync(
            () =>
            {
                MessageBox.Show(GetOwner(), message, title, MessageBoxButton.OK, MessageBoxImage.Error);
                return 0;
            },
            cancellationToken);
    }

    public async Task ShowInfoAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        await InvokeAsync(
            () =>
            {
                MessageBox.Show(GetOwner(), message, title, MessageBoxButton.OK, MessageBoxImage.Information);
                return 0;
            },
            cancellationToken);
    }

    private static Window? GetOwner()
    {
        return Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
            ?? Application.Current.MainWindow;
    }

    private static async Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        if (Application.Current.Dispatcher.CheckAccess())
        {
            return action();
        }

        var operation = Application.Current.Dispatcher.InvokeAsync(action);
        using var registration = cancellationToken.Register(() => operation.Abort());
        return await operation.Task;
    }
}
