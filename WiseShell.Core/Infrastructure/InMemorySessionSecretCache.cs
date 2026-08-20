using System.Collections.Concurrent;
using WiseShell.Core.Interfaces;

namespace WiseShell.Core.Infrastructure;

public sealed class InMemorySessionSecretCache : ISessionSecretCache
{
    private readonly ConcurrentDictionary<Guid, string?> _secrets = new();

    public bool TryGetSecret(Guid sessionId, out string? secret)
    {
        return _secrets.TryGetValue(sessionId, out secret);
    }

    public void StoreSecret(Guid sessionId, string? secret)
    {
        _secrets[sessionId] = secret;
    }

    public void RemoveSecret(Guid sessionId)
    {
        _secrets.TryRemove(sessionId, out _);
    }

    public void Clear()
    {
        _secrets.Clear();
    }
}
