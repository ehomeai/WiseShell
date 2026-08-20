using System.Text.Json;
using WiseShell.Core.Interfaces;
using WiseShell.Core.Models;

namespace WiseShell.Core.Infrastructure;

public sealed class JsonHostKeyTrustStore : IHostKeyTrustStore
{
    private readonly string _knownHostsFile;

    public JsonHostKeyTrustStore(string? baseDirectory = null)
    {
        _knownHostsFile = AppDataPaths.GetKnownHostsFile(baseDirectory);
    }

    public async Task<bool> IsTrustedAsync(TrustedHostKey hostKey, CancellationToken cancellationToken = default)
    {
        var store = await LoadAsync(cancellationToken);
        return store.Any(existing =>
            string.Equals(existing.Host, hostKey.Host, StringComparison.OrdinalIgnoreCase) &&
            existing.Port == hostKey.Port &&
            string.Equals(existing.Algorithm, hostKey.Algorithm, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.FingerprintSha256, hostKey.FingerprintSha256, StringComparison.Ordinal));
    }

    public async Task TrustAsync(TrustedHostKey hostKey, CancellationToken cancellationToken = default)
    {
        var store = await LoadAsync(cancellationToken);
        if (store.Any(existing =>
                string.Equals(existing.Host, hostKey.Host, StringComparison.OrdinalIgnoreCase) &&
                existing.Port == hostKey.Port &&
                string.Equals(existing.Algorithm, hostKey.Algorithm, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.FingerprintSha256, hostKey.FingerprintSha256, StringComparison.Ordinal)))
        {
            return;
        }

        store.Add(hostKey);
        Directory.CreateDirectory(Path.GetDirectoryName(_knownHostsFile)!);
        await using var stream = File.Create(_knownHostsFile);
        await JsonSerializer.SerializeAsync(stream, store, JsonOptionsProvider.Json, cancellationToken);
    }

    private async Task<List<TrustedHostKey>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_knownHostsFile))
        {
            return new List<TrustedHostKey>();
        }

        await using var stream = File.OpenRead(_knownHostsFile);
        var result = await JsonSerializer.DeserializeAsync<List<TrustedHostKey>>(stream, JsonOptionsProvider.Json, cancellationToken);
        return result ?? new List<TrustedHostKey>();
    }
}
