namespace WiseShell.Core.Interfaces;

public interface ISessionSecretCache
{
    bool TryGetSecret(Guid sessionId, out string? secret);

    void StoreSecret(Guid sessionId, string? secret);

    void RemoveSecret(Guid sessionId);

    void Clear();
}
