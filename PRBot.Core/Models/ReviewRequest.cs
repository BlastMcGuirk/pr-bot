namespace PRBot.Core.Models;

public sealed record ReviewRequest(
    Uri PullRequestUrl,
    string PersonalAccessToken,
    string AgentInstructions,
    string LlmHostId,
    Uri LlmEndpoint,
    string ModelId);
