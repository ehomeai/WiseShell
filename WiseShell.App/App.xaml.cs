using System.Windows;
using WiseShell.App.Services;
using WiseShell.App.ViewModels;
using WiseShell.App.Windows;
using WiseShell.Core.Infrastructure;
using WiseShell.Core.Interfaces;
using WiseShell.Core.Models;
using WiseShell.Transport.SshNet.Services;

namespace WiseShell.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var sessionRepository = new JsonSessionRepository();
        var credentialStore = new DpapiCredentialStore();
        var sessionSecretCache = new InMemorySessionSecretCache();
        var hostKeyTrustStore = new JsonHostKeyTrustStore();

        IDialogService dialogService = new DialogService();
        ISftpSessionFactory sftpSessionFactory = new SshNetSftpSessionFactory(hostKeyTrustStore);
        Func<ITerminalSession> terminalFactory = () => new SshNetTerminalSession(hostKeyTrustStore);

        var viewModel = new MainWindowViewModel(
            Dispatcher,
            sessionRepository,
            credentialStore,
            sessionSecretCache,
            dialogService,
            sftpSessionFactory,
            terminalFactory,
            request => OpenSftpWindowAsync(request, sftpSessionFactory, sessionSecretCache, dialogService));

        var window = new MainWindow
        {
            DataContext = viewModel,
        };

        MainWindow = window;
        window.Show();
    }

    private static Task OpenSftpWindowAsync(
        SftpConnectionRequest request,
        ISftpSessionFactory sftpSessionFactory,
        ISessionSecretCache sessionSecretCache,
        IDialogService dialogService)
    {
        var viewModel = new SftpBrowserViewModel(
            Current.Dispatcher,
            request,
            sftpSessionFactory,
            sessionSecretCache,
            dialogService);

        var window = new SftpBrowserWindow
        {
            DataContext = viewModel,
            Owner = Current.MainWindow,
        };

        window.Show();
        return window.InitializeAsync();
    }
}
