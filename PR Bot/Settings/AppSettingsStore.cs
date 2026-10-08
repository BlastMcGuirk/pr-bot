using System.IO;
using System.Text.Json;

namespace PR_Bot.Settings;

public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _settingsPath;

    public AppSettingsStore(string? settingsPath = null)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PR Bot");
        Directory.CreateDirectory(directory);
        _settingsPath = settingsPath ?? Path.Combine(directory, "settings.json");
        DefaultAgentInstructionsPath = Path.Combine(directory, "AGENTS.md");
    }

    public string DefaultAgentInstructionsPath { get; }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        AppSettings settings;
        if (File.Exists(_settingsPath))
        {
            await using var stream = File.OpenRead(_settingsPath);
            settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken)
                       ?? new AppSettings();
        }
        else
        {
            settings = new AppSettings();
        }

        settings.LlmEndpoints ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        settings.PullRequestSources ??= [];
        settings.LlmEndpoints.TryAdd("ollama", "http://localhost:11434");
        settings.LlmEndpoints.TryAdd("lm-studio", "http://localhost:1234/v1");
        if (string.IsNullOrWhiteSpace(settings.AgentInstructionsPath))
        {
            settings.AgentInstructionsPath = DefaultAgentInstructionsPath;
        }

        var instructionDirectory = Path.GetDirectoryName(settings.AgentInstructionsPath);
        if (!string.IsNullOrWhiteSpace(instructionDirectory))
        {
            Directory.CreateDirectory(instructionDirectory);
        }

        if (!File.Exists(settings.AgentInstructionsPath))
        {
            await File.WriteAllTextAsync(settings.AgentInstructionsPath, DefaultInstructions, cancellationToken);
        }

        return settings;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await using var stream = File.Create(_settingsPath);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
    }

    public static Task<string> ReadAgentInstructionsAsync(string path, CancellationToken cancellationToken = default) =>
        File.ReadAllTextAsync(path, cancellationToken);

    public static Task SaveAgentInstructionsAsync(string path, string instructions, CancellationToken cancellationToken = default) =>
        File.WriteAllTextAsync(path, instructions, cancellationToken);

    private const string DefaultInstructions = "# PR review instructions\n\nAdd project-specific review rules, coding standards, and context here. Only the pull request diff and this file will be provided to the review agent.\n";
}
