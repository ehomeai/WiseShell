namespace WiseShell.Core.Models;

public sealed class TerminalSessionStartRequest
{
    public required SessionProfile Profile { get; init; }

    public string? Secret { get; init; }

    public int InitialColumns { get; init; } = 120;

    public int InitialRows { get; init; } = 32;

    public Func<HostKeyVerificationRequest, CancellationToken, Task<bool>>? HostKeyVerificationCallback { get; init; }
}
