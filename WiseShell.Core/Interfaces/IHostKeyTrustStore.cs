using WiseShell.Core.Models;

namespace WiseShell.Core.Interfaces;

public interface IHostKeyTrustStore
{
    Task<bool> IsTrustedAsync(TrustedHostKey hostKey, CancellationToken cancellationToken = default);

    Task TrustAsync(TrustedHostKey hostKey, CancellationToken cancellationToken = default);
}
