using System.Runtime.Versioning;
using WiseShell.Core.Infrastructure;

namespace WiseShell.Tests;

[SupportedOSPlatform("windows")]
public sealed class CredentialStoreTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "WiseShell.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAndGetPasswordAsync_RoundTripsSecret()
    {
        var store = new DpapiCredentialStore(_tempDirectory);
        var sessionId = Guid.NewGuid();

        await store.SavePasswordAsync(sessionId, "admin123!");
        var password = await store.GetPasswordAsync(sessionId);

        Assert.Equal("admin123!", password);
    }

    [Fact]
    public async Task DeletePasswordAsync_RemovesPersistedSecret()
    {
        var store = new DpapiCredentialStore(_tempDirectory);
        var sessionId = Guid.NewGuid();

        await store.SavePasswordAsync(sessionId, "temporary-secret");
        await store.DeletePasswordAsync(sessionId);

        var password = await store.GetPasswordAsync(sessionId);
        Assert.Null(password);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
