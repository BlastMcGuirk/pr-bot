using System.IO;
using System.Text;
using PR_Bot.Settings;
using PRBot.Core.Models;
using PRBot.Core.Sources;

namespace PRBot.Core.Tests;

public sealed class SettingsPersistenceTests
{
    [Fact]
    public async Task NewSettingsHaveNoConfiguredPullRequestSources()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new AppSettingsStore(Path.Combine(directory, "settings.json"));
            await store.SaveAsync(new AppSettings { AgentInstructionsPath = Path.Combine(directory, "AGENTS.md") });

            var loadedSettings = await store.LoadAsync();

            Assert.Empty(loadedSettings.PullRequestSources);
            Assert.Equal(0, loadedSettings.OllamaContextSize);
            Assert.Equal(10, loadedSettings.ReviewRequestTimeoutMinutes);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

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
                OllamaContextSize = 16384,
                ReviewRequestTimeoutMinutes = 45,
                LlmEndpoints = new Dictionary<string, string> { ["ollama"] = "http://localhost:11434" },
                AgentInstructionsPath = Path.Combine(directory, "AGENTS.md"),
                PullRequestSources =
                [
                    new PullRequestSourceConfiguration
                    {
                        SourceTypeId = PullRequestSourceCatalog.BitbucketCloudId,
                        SourceUrl = "bitbucket.org",
                        AuthenticationMethod = SourceAuthenticationMethod.UsernamePassword
                    }
                ]
            };
            await store.SaveAsync(settings);

            var contents = await File.ReadAllTextAsync(Path.Combine(directory, "settings.json"));
            var loadedSettings = await store.LoadAsync();

            Assert.Contains("qwen2.5-coder", contents);
            Assert.Equal(settings.SelectedLlmHostId, loadedSettings.SelectedLlmHostId);
            Assert.Equal(settings.SelectedModelId, loadedSettings.SelectedModelId);
            Assert.Equal(settings.OllamaContextSize, loadedSettings.OllamaContextSize);
            Assert.Equal(settings.ReviewRequestTimeoutMinutes, loadedSettings.ReviewRequestTimeoutMinutes);
            Assert.Equal("bitbucket.org", loadedSettings.PullRequestSources.Single().SourceUrl);
            Assert.Equal(SourceAuthenticationMethod.UsernamePassword, loadedSettings.PullRequestSources.Single().AuthenticationMethod);
            Assert.DoesNotContain("personalAccessToken", contents, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret-value", contents, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SettingsWithoutOllamaContextSizeUseHostDefault()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(settingsPath, System.Text.Json.JsonSerializer.Serialize(new
            {
                AgentInstructionsPath = Path.Combine(directory, "AGENTS.md")
            }));
            var store = new AppSettingsStore(settingsPath);

            var loadedSettings = await store.LoadAsync();

            Assert.Equal(0, loadedSettings.OllamaContextSize);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DpapiCredentialStoreRoundTripsSourceCredentialsWithoutWritingPlaintext()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var filePath = Path.Combine(directory, "source-credentials.dat");
            var store = new DpapiSourceCredentialStore(filePath);
            var rowId = Guid.NewGuid();
            var credential = new SourceCredential { Username = "private-user", Password = "private-password" };
            var credentials = new SourceCredentialCollection { Entries = { [rowId] = credential } };

            await store.SaveAsync(credentials);

            var fileContents = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(filePath));
            Assert.DoesNotContain(credential.Username, fileContents, StringComparison.Ordinal);
            Assert.DoesNotContain(credential.Password, fileContents, StringComparison.Ordinal);
            var loaded = await store.LoadAsync();
            Assert.Equal(credential.Username, loaded.Entries[rowId].Username);
            Assert.Equal(credential.Password, loaded.Entries[rowId].Password);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
