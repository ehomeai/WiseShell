namespace WiseShell.Core.Models;

public sealed class SftpConnectionRequest
{
    public required SessionProfile Profile { get; init; }

    public string? Secret { get; init; }

    public Func<HostKeyVerificationRequest, CancellationToken, Task<bool>>? HostKeyVerificationCallback { get; init; }
}
