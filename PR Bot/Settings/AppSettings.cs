using PRBot.Core.Models;

namespace PR_Bot.Settings;

public sealed class AppSettings
{
    public string SelectedLlmHostId { get; set; } = "ollama";

    public string SelectedModelId { get; set; } = string.Empty;

    public int OllamaContextSize { get; set; }

    public int ReviewRequestTimeoutMinutes { get; set; } = 10;

    public Dictionary<string, string> LlmEndpoints { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ollama"] = "http://localhost:11434",
        ["lm-studio"] = "http://localhost:1234/v1"
    };

    public string AgentInstructionsPath { get; set; } = string.Empty;

    public List<PullRequestSourceConfiguration> PullRequestSources { get; set; } = [];
}
