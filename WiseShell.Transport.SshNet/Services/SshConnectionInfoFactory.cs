using Renci.SshNet;
using Renci.SshNet.Common;
using WiseShell.Core.Enums;
using WiseShell.Core.Models;

namespace WiseShell.Transport.SshNet.Services;

internal static class SshConnectionInfoFactory
{
    public static ConnectionInfo CreateConnectionInfo(SessionProfile profile, string? secret)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (string.IsNullOrWhiteSpace(profile.Host))
        {
            throw new InvalidOperationException("主机地址不能为空。");
        }

        if (string.IsNullOrWhiteSpace(profile.Username))
        {
            throw new InvalidOperationException("用户名不能为空。");
        }

        AuthenticationMethod authenticationMethod = profile.AuthType switch
        {
            AuthenticationType.Password => new PasswordAuthenticationMethod(
                profile.Username,
                string.IsNullOrEmpty(secret)
                    ? throw new InvalidOperationException("密码登录需要提供密码。")
                    : secret),
            AuthenticationType.PrivateKey => new PrivateKeyAuthenticationMethod(
                profile.Username,
                BuildPrivateKey(profile.PrivateKeyPath, secret)),
            _ => throw new InvalidOperationException($"不支持的认证类型: {profile.AuthType}"),
        };

        return new ConnectionInfo(profile.Host, profile.Port, profile.Username, authenticationMethod);
    }

    public static TrustedHostKey ToTrustedHostKey(SessionProfile profile, HostKeyEventArgs args)
    {
        return new TrustedHostKey
        {
            Host = profile.Host,
            Port = profile.Port,
            Algorithm = args.HostKeyName ?? string.Empty,
            FingerprintSha256 = args.FingerPrintSHA256 ?? string.Empty,
            FingerprintMd5 = args.FingerPrintMD5?.ToLowerInvariant() ?? string.Empty,
            KeyLength = args.KeyLength,
        };
    }

    public static HostKeyVerificationRequest ToVerificationRequest(SessionProfile profile, HostKeyEventArgs args)
    {
        return new HostKeyVerificationRequest
        {
            Host = profile.Host,
            Port = profile.Port,
            Algorithm = args.HostKeyName ?? string.Empty,
            FingerprintSha256 = args.FingerPrintSHA256 ?? string.Empty,
            FingerprintMd5 = args.FingerPrintMD5?.ToLowerInvariant() ?? string.Empty,
            KeyLength = args.KeyLength,
        };
    }

    private static PrivateKeyFile BuildPrivateKey(string privateKeyPath, string? secret)
    {
        if (string.IsNullOrWhiteSpace(privateKeyPath))
        {
            throw new InvalidOperationException("私钥认证需要提供私钥文件路径。");
        }

        if (!File.Exists(privateKeyPath))
        {
            throw new FileNotFoundException("找不到私钥文件。", privateKeyPath);
        }

        return string.IsNullOrEmpty(secret)
            ? new PrivateKeyFile(privateKeyPath)
            : new PrivateKeyFile(privateKeyPath, secret);
    }
}
