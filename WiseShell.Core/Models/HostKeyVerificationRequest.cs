namespace WiseShell.Core.Models;

public sealed class HostKeyVerificationRequest
{
    public string Host { get; init; } = string.Empty;

    public int Port { get; init; }

    public string Algorithm { get; init; } = string.Empty;

    public string FingerprintSha256 { get; init; } = string.Empty;

    public string FingerprintMd5 { get; init; } = string.Empty;

    public int KeyLength { get; init; }
}
