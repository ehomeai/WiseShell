namespace WiseShell.Core.Models;

public sealed class TrustedHostKey
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }

    public string Algorithm { get; set; } = string.Empty;

    public string FingerprintSha256 { get; set; } = string.Empty;

    public string FingerprintMd5 { get; set; } = string.Empty;

    public int KeyLength { get; set; }
}
