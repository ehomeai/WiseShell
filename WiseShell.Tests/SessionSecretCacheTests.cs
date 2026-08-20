using WiseShell.Core.Infrastructure;

namespace WiseShell.Tests;

public sealed class SessionSecretCacheTests
{
    [Fact]
    public void StoreSecret_RoundTripsValue()
    {
        var cache = new InMemorySessionSecretCache();
        var sessionId = Guid.NewGuid();

        cache.StoreSecret(sessionId, "secret-value");

        var found = cache.TryGetSecret(sessionId, out var secret);

        Assert.True(found);
        Assert.Equal("secret-value", secret);
    }

    [Fact]
    public void StoreSecret_AllowsNullValue()
    {
        var cache = new InMemorySessionSecretCache();
        var sessionId = Guid.NewGuid();

        cache.StoreSecret(sessionId, null);

        var found = cache.TryGetSecret(sessionId, out var secret);

        Assert.True(found);
        Assert.Null(secret);
    }

    [Fact]
    public void RemoveSecret_RemovesEntry()
    {
        var cache = new InMemorySessionSecretCache();
        var sessionId = Guid.NewGuid();
        cache.StoreSecret(sessionId, "temporary");

        cache.RemoveSecret(sessionId);

        Assert.False(cache.TryGetSecret(sessionId, out _));
    }
}
