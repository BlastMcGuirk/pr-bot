using System.IO;
using System.Text;
using PR_Bot.Settings;

namespace PRBot.Core.Tests;

public sealed class SettingsPersistenceTests
{
    [Fact]
    public async Task SettingsFilePersistsPreferencesWithoutPersonalAccessTokens()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new AppSettingsStore(Path.Combine(directory, "settings.json"));
            var settings = new AppSettings
            {
                SelectedLlmHostId = "ollama",
                SelectedModelId = "qwen2.5-coder",
                LlmEndpoints = new Dictionary<string, string> { ["ollama"] = "http://localhost:11434" },
                AgentInstructionsPath = Path.Combine(directory, "AGENTS.md")
            };
            await store.SaveAsync(settings);

            var contents = await File.ReadAllTextAsync(Path.Combine(directory, "settings.json"));
            var loadedSettings = await store.LoadAsync();

            Assert.Contains("qwen2.5-coder", contents);
            Assert.Equal(settings.SelectedLlmHostId, loadedSettings.SelectedLlmHostId);
            Assert.Equal(settings.SelectedModelId, loadedSettings.SelectedModelId);
            Assert.DoesNotContain("personalAccessToken", contents, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("azureDevOps", contents, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("bitbucket", contents, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DpapiCredentialStoreRoundTripsTokensWithoutWritingPlaintext()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var filePath = Path.Combine(directory, "credentials.dat");
            var store = new DpapiPersonalAccessTokenStore(filePath);
            var tokens = new PersonalAccessTokens("ado-secret-value", "bitbucket-secret-value");

            await store.SaveAsync(tokens);

            var fileContents = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(filePath));
            Assert.DoesNotContain(tokens.AzureDevOps, fileContents, StringComparison.Ordinal);
            Assert.DoesNotContain(tokens.Bitbucket, fileContents, StringComparison.Ordinal);
            Assert.Equal(tokens, await store.LoadAsync());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
