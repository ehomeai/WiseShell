namespace WiseShell.Core.Interfaces;

public interface ICredentialStore
{
    Task<string?> GetPasswordAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task SavePasswordAsync(Guid sessionId, string password, CancellationToken cancellationToken = default);

    Task DeletePasswordAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
