using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace PR_Bot.Settings;

public sealed class DpapiPersonalAccessTokenStore : IPersonalAccessTokenStore
{
    private static readonly byte[] Entropy = "PR Bot local credentials v1"u8.ToArray();
    private readonly string _path;

    public DpapiPersonalAccessTokenStore(string? path = null)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PR Bot");
        Directory.CreateDirectory(directory);
        _path = path ?? Path.Combine(directory, "credentials.dat");
    }

    public async Task<PersonalAccessTokens> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return new PersonalAccessTokens(string.Empty, string.Empty);
        }

        var protectedBytes = await File.ReadAllBytesAsync(_path, cancellationToken);
        var clearBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<PersonalAccessTokens>(clearBytes)
               ?? new PersonalAccessTokens(string.Empty, string.Empty);
    }

    public async Task SaveAsync(PersonalAccessTokens tokens, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        var clearBytes = JsonSerializer.SerializeToUtf8Bytes(tokens);
        var protectedBytes = ProtectedData.Protect(clearBytes, Entropy, DataProtectionScope.CurrentUser);
        var temporaryPath = _path + ".tmp";
        await File.WriteAllBytesAsync(temporaryPath, protectedBytes, cancellationToken);
        File.Move(temporaryPath, _path, overwrite: true);
        CryptographicOperations.ZeroMemory(clearBytes);
    }
}
