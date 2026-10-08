using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace PR_Bot.Settings;

public sealed class DpapiSourceCredentialStore : ISourceCredentialStore
{
    private static readonly byte[] Entropy = "PR Bot source credentials v1"u8.ToArray();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _path;

    public DpapiSourceCredentialStore(string? path = null)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PR Bot");
        Directory.CreateDirectory(directory);
        _path = path ?? Path.Combine(directory, "source-credentials.dat");
    }

    public async Task<SourceCredentialCollection> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return new SourceCredentialCollection();
        }

        var protectedBytes = await File.ReadAllBytesAsync(_path, cancellationToken);
        var clearBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            return JsonSerializer.Deserialize<SourceCredentialCollection>(clearBytes, JsonOptions)
                   ?? new SourceCredentialCollection();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
        }
    }

    public async Task SaveAsync(SourceCredentialCollection credentials, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        var clearBytes = JsonSerializer.SerializeToUtf8Bytes(credentials, JsonOptions);
        try
        {
            var protectedBytes = ProtectedData.Protect(clearBytes, Entropy, DataProtectionScope.CurrentUser);
            var temporaryPath = _path + ".tmp";
            await File.WriteAllBytesAsync(temporaryPath, protectedBytes, cancellationToken);
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
        }
    }
}
