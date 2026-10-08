namespace PRBot.Core.Models;

public sealed record ReviewRequest(
    Uri PullRequestUrl,
    SourceAuthenticationMethod AuthenticationMethod,
    string Username,
    string Credential,
    string AgentInstructions,
    string LlmHostId,
    Uri LlmEndpoint,
    string ModelId)
{
    public int OllamaContextSize { get; init; }

    public int ReviewRequestTimeoutMinutes { get; init; } = 10;

    public ReviewRequest(
        Uri pullRequestUrl,
        string personalAccessToken,
        string agentInstructions,
        string llmHostId,
        Uri llmEndpoint,
        string modelId)
        : this(
            pullRequestUrl,
            SourceAuthenticationMethod.PersonalAccessToken,
            string.Empty,
            personalAccessToken,
            agentInstructions,
            llmHostId,
            llmEndpoint,
            modelId)
    {
    }
}
