using System.IO;
using System.Windows;
using WiseShell.App.Services;
using WiseShell.Core.Enums;
using WiseShell.Core.Models;

namespace WiseShell.App.Dialogs;

public partial class SecretPromptWindow : Window
{
    private readonly SecretPromptRequest _request;

    public SecretPromptWindow(SecretPromptRequest request)
    {
        InitializeComponent();
        _request = request;

        Title = "SSH用户身份验证";
        Configure(request);
        Loaded += OnLoaded;
    }

    public string Secret => SecretPasswordBox.Password;

    public bool RememberSecret => RememberSecretCheckBox.IsChecked == true;

    private void Configure(SecretPromptRequest request)
    {
        var profile = request.Profile;
        HostText.Text = BuildHostText(profile);
        UsernameText.Text = string.IsNullOrWhiteSpace(profile.Username) ? "(鏈～鍐?" : profile.Username;
        ServiceTypeText.Text = BuildServiceTypeText(profile);
        PromptMessage.Text = request.Message;

        var isPassword = profile.AuthType == AuthenticationType.Password;
        CredentialSectionTitle.Text = isPassword ? "Password" : "Private Key";
        SecretLabel.Text = isPassword ? "密码:" : "口令:";
        RememberSecretCheckBox.Content = isPassword ? "记住密码" : "记住口令";
        RememberSecretCheckBox.IsChecked = request.RememberSecret;

        if (profile.AuthType == AuthenticationType.PrivateKey)
        {
            PrivateKeyPathLabel.Visibility = Visibility.Visible;
            PrivateKeyPathTextBox.Visibility = Visibility.Visible;
            PrivateKeyPathTextBox.Text = BuildPrivateKeyPathText(profile.PrivateKeyPath);
        }

        if (request.AllowEmpty)
        {
            HintText.Text = "如果私钥没有口令，可以留空后直接确定。";
            HintText.Visibility = Visibility.Visible;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SecretPasswordBox.Focus();
    }

    private void Confirm_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_request.AllowEmpty && string.IsNullOrWhiteSpace(SecretPasswordBox.Password))
        {
            var promptName = _request.Profile.AuthType == AuthenticationType.Password ? "密码" : "口令";
            MessageBox.Show(this, $"请输入{promptName}。", "不能为空", MessageBoxButton.OK, MessageBoxImage.Warning);
            SecretPasswordBox.Focus();
            return;
        }

        DialogResult = true;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private static string BuildHostText(SessionProfile profile)
    {
        var endpoint = string.IsNullOrWhiteSpace(profile.Host)
            ? "(鏈厤缃富鏈?"
            : $"{profile.Host}:{profile.Port}";

        return string.IsNullOrWhiteSpace(profile.Name)
            ? endpoint
            : $"{endpoint} ({profile.Name})";
    }

    private static string BuildServiceTypeText(SessionProfile profile)
    {
        return profile.AuthType switch
        {
            AuthenticationType.PrivateKey => "SSH2 / 私钥认证",
            _ => "SSH2 / 密码认证",
        };
    }

    private static string BuildPrivateKeyPathText(string? privateKeyPath)
    {
        if (string.IsNullOrWhiteSpace(privateKeyPath))
        {
            return "(鏈厤缃閽ユ枃浠?";
        }

        try
        {
            return Path.GetFullPath(privateKeyPath);
        }
        catch (Exception)
        {
            return privateKeyPath;
        }
    }
}
