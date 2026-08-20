using WiseShell.Core.Infrastructure;
using WiseShell.Core.Models;

namespace WiseShell.Tests;

public sealed class HostKeyTrustStoreTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "WiseShell.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task TrustAsync_MarksKnownHostAsTrusted()
    {
        var store = new JsonHostKeyTrustStore(_tempDirectory);
        var knownHost = new TrustedHostKey
        {
            Host = "example.com",
            Port = 22,
            Algorithm = "ssh-ed25519",
            FingerprintSha256 = "SHA256:abcdef123456",
            FingerprintMd5 = "aa:bb:cc",
            KeyLength = 256,
        };

        await store.TrustAsync(knownHost);

        Assert.True(await store.IsTrustedAsync(knownHost));
    }

    [Fact]
    public async Task IsTrustedAsync_RequiresMatchingFingerprint()
    {
        var store = new JsonHostKeyTrustStore(_tempDirectory);
        await store.TrustAsync(new TrustedHostKey
        {
            Host = "example.com",
            Port = 22,
            Algorithm = "ssh-ed25519",
            FingerprintSha256 = "SHA256:trusted",
            FingerprintMd5 = "aa:bb:cc",
            KeyLength = 256,
        });

        var isTrusted = await store.IsTrustedAsync(new TrustedHostKey
        {
            Host = "example.com",
            Port = 22,
            Algorithm = "ssh-ed25519",
            FingerprintSha256 = "SHA256:different",
            FingerprintMd5 = "aa:bb:cc",
            KeyLength = 256,
        });

        Assert.False(isTrusted);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
