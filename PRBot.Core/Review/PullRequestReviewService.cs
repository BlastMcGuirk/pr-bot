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
            request.AuthenticationMethod,
            request.Username,
            request.Credential,
            cancellationToken);
        return await ReviewDiffCoreAsync(
            pullRequest.DiffText,
            pullRequest.PullRequestUrl,
            pullRequest.Title,
            request.AgentInstructions,
            llmHost,
            request.LlmEndpoint,
            request.ModelId,
            cancellationToken);
    }

    public async Task<PullRequestReviewResult> ReviewDiffAsync(
        string diffText,
        string agentInstructions,
        string llmHostId,
        Uri llmEndpoint,
        string modelId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diffText);
        var llmHost = _llmHosts.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, llmHostId, StringComparison.OrdinalIgnoreCase))
            ?? throw new NotSupportedException($"LLM host '{llmHostId}' is not registered.");

        return await ReviewDiffCoreAsync(
            diffText,
            null,
            "Local diff review",
            agentInstructions,
            llmHost,
            llmEndpoint,
            modelId,
            cancellationToken);
    }

    private async Task<PullRequestReviewResult> ReviewDiffCoreAsync(
        string diffText,
        Uri? pullRequestUrl,
        string title,
        string agentInstructions,
        ILocalLlmHost llmHost,
        Uri llmEndpoint,
        string modelId,
        CancellationToken cancellationToken)
    {
        var chatClient = llmHost.CreateChatClient(llmEndpoint, modelId);
        var agent = agentFactory.Create(chatClient);
        var items = await agent.ReviewAsync(diffText, agentInstructions, cancellationToken);
        var orderedItems = items
            .OrderBy(item => item.Severity)
            .ThenBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.LineNumber)
            .ToArray();

        return new PullRequestReviewResult(pullRequestUrl, title, orderedItems);
    }
}
