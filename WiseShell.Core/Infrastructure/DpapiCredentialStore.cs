using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using WiseShell.Core.Interfaces;

namespace WiseShell.Core.Infrastructure;

[SupportedOSPlatform("windows")]
public sealed class DpapiCredentialStore : ICredentialStore
{
    private readonly string _credentialsFile;

    public DpapiCredentialStore(string? baseDirectory = null)
    {
        _credentialsFile = AppDataPaths.GetCredentialsFile(baseDirectory);
    }

    public async Task<string?> GetPasswordAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var store = await LoadAsync(cancellationToken);
        if (!store.TryGetValue(sessionId.ToString("N"), out var protectedValue))
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(protectedValue);
            var plaintext = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public async Task SavePasswordAsync(Guid sessionId, string password, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var store = await LoadAsync(cancellationToken);
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(password), null, DataProtectionScope.CurrentUser);
        store[sessionId.ToString("N")] = Convert.ToBase64String(protectedBytes);
        await SaveAsync(store, cancellationToken);
    }

    public async Task DeletePasswordAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var store = await LoadAsync(cancellationToken);
        if (store.Remove(sessionId.ToString("N")))
        {
            await SaveAsync(store, cancellationToken);
        }
    }

    private async Task<Dictionary<string, string>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_credentialsFile))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        await using var stream = File.OpenRead(_credentialsFile);
        var result = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, JsonOptionsProvider.Json, cancellationToken);
        return result ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private async Task SaveAsync(Dictionary<string, string> store, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_credentialsFile)!);
        await using var stream = File.Create(_credentialsFile);
        await JsonSerializer.SerializeAsync(stream, store, JsonOptionsProvider.Json, cancellationToken);
    }
}
