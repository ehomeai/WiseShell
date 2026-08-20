using System.Windows;
using Microsoft.Win32;
using WiseShell.App.Infrastructure;
using WiseShell.App.ViewModels;
using WiseShell.Core.Enums;
using WiseShell.Core.Models;

namespace WiseShell.App.Dialogs;

public partial class SessionEditWindow : Window
{
    private bool _secretWasEdited;
    private readonly Func<SessionProfile, CancellationToken, Task<string?>>? _browseRemoteDirectoryAsync;

    public SessionEditWindow(
        SessionEditViewModel viewModel,
        Func<SessionProfile, CancellationToken, Task<string?>>? browseRemoteDirectoryAsync = null)
    {
        InitializeComponent();
        DataContext = viewModel;
        _browseRemoteDirectoryAsync = browseRemoteDirectoryAsync;
        AuthTypeComboBox.ItemsSource = Enum.GetValues<AuthenticationType>();
        ThemeComboBox.ItemsSource = Enum.GetValues<TerminalTheme>();
        FontFamilyComboBox.ItemsSource = TerminalFontCatalog.GetRecommendedFontFamilies();
    }

    public SessionEditViewModel ViewModel => (SessionEditViewModel)DataContext;

    public string Secret => SecretPasswordBox.Password;

    public bool SecretWasEdited => _secretWasEdited;

    private void SecretPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        _secretWasEdited = true;
    }

    private void BrowsePrivateKey_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择私钥文件",
            Filter = "私钥文件|*.pem;*.ppk;*.key;id_rsa;id_ed25519|所有文件|*.*",
        };

        if (dialog.ShowDialog(this) == true)
        {
            ViewModel.PrivateKeyPath = dialog.FileName;
        }
    }

    private void BrowseLocalDirectory_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择默认本地目录",
        };

        if (!string.IsNullOrWhiteSpace(ViewModel.LocalStartupDirectory))
        {
            dialog.InitialDirectory = ViewModel.LocalStartupDirectory;
        }

        if (dialog.ShowDialog(this) == true)
        {
            ViewModel.LocalStartupDirectory = dialog.FolderName;
        }
    }

    private async void BrowseRemoteDirectory_OnClick(object sender, RoutedEventArgs e)
    {
        if (_browseRemoteDirectoryAsync is null)
        {
            return;
        }

        try
        {
            var selectedDirectory = await _browseRemoteDirectoryAsync(ViewModel.ToProfile(), CancellationToken.None);
            if (!string.IsNullOrWhiteSpace(selectedDirectory))
            {
                ViewModel.StartupDirectory = selectedDirectory;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "选择远程目录失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        var profile = ViewModel.ToProfile();
        if (string.IsNullOrWhiteSpace(profile.Name) ||
            string.IsNullOrWhiteSpace(profile.Host) ||
            string.IsNullOrWhiteSpace(profile.Username))
        {
            MessageBox.Show(this, "名称、主机和用户名不能为空。", "表单不完整", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (profile.AuthType == AuthenticationType.PrivateKey && string.IsNullOrWhiteSpace(profile.PrivateKeyPath))
        {
            MessageBox.Show(this, "私钥认证必须提供私钥文件路径。", "表单不完整", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (profile.FontSize < 9d || profile.FontSize > 36d)
        {
            MessageBox.Show(this, "终端字号请设置在 9 到 36 之间。", "表单不完整", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(profile.TerminalFontFamily))
        {
            MessageBox.Show(this, "终端字体不能为空。", "表单不完整", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
