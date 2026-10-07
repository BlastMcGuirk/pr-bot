using PRBot.Core.Abstractions;
using PRBot.Core.Models;

namespace PRBot.Core.Review;

public sealed class PullRequestReviewService(
    IEnumerable<IPullRequestSource> sources,
    IEnumerable<ILocalLlmHost> llmHosts,
    IPrReviewAgentFactory agentFactory)
{
    private readonly IReadOnlyList<IPullRequestSource> _sources = sources.ToArray();
    private readonly IReadOnlyList<ILocalLlmHost> _llmHosts = llmHosts.ToArray();

    public async Task<PullRequestReviewResult> ReviewAsync(
        ReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var source = _sources.SingleOrDefault(candidate => candidate.CanHandle(request.PullRequestUrl))
            ?? throw new NotSupportedException($"No pull request source supports '{request.PullRequestUrl.Host}'.");
        var llmHost = _llmHosts.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, request.LlmHostId, StringComparison.OrdinalIgnoreCase))
            ?? throw new NotSupportedException($"LLM host '{request.LlmHostId}' is not registered.");

        var pullRequest = await source.FetchDiffAsync(
            request.PullRequestUrl,
            request.PersonalAccessToken,
            cancellationToken);
        var chatClient = llmHost.CreateChatClient(request.LlmEndpoint, request.ModelId);
        var agent = agentFactory.Create(chatClient);
        var items = await agent.ReviewAsync(
            pullRequest.DiffText,
            request.AgentInstructions,
            cancellationToken);

        var orderedItems = items
            .OrderBy(item => item.Severity)
            .ThenBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.LineNumber)
            .ToArray();

        return new PullRequestReviewResult(pullRequest.PullRequestUrl, pullRequest.Title, orderedItems);
    }
}
