using PRBot.Core.Abstractions;
using PRBot.Core.Models;

namespace PRBot.Core.Review;

public sealed class PullRequestReviewService(
    IEnumerable<IPullRequestSource> sources,
    IEnumerable<ILocalLlmHost> llmHosts,
    IPrReviewAgentFactory agentFactory,
    TimeProvider? timeProvider = null)
{
    private const int MaximumReviewRequestTimeoutMinutes = 71582;
    private readonly IReadOnlyList<IPullRequestSource> _sources = sources.ToArray();
    private readonly IReadOnlyList<ILocalLlmHost> _llmHosts = llmHosts.ToArray();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<PullRequestReviewResult> ReviewAsync(
        ReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateReviewRequestTimeout(request.ReviewRequestTimeoutMinutes);

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
            request.OllamaContextSize,
            request.ReviewRequestTimeoutMinutes,
            cancellationToken);
    }

    public async Task<PullRequestReviewResult> ReviewDiffAsync(
        string diffText,
        string agentInstructions,
        string llmHostId,
        Uri llmEndpoint,
        string modelId,
        int ollamaContextSize = 0,
        CancellationToken cancellationToken = default,
        int reviewRequestTimeoutMinutes = 10)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diffText);
        ValidateReviewRequestTimeout(reviewRequestTimeoutMinutes);
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
            ollamaContextSize,
            reviewRequestTimeoutMinutes,
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
        int ollamaContextSize,
        int reviewRequestTimeoutMinutes,
        CancellationToken cancellationToken)
    {
        if (ollamaContextSize < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ollamaContextSize));
        }

        var contextSize = llmHost.Id.Equals("ollama", StringComparison.OrdinalIgnoreCase)
            ? ollamaContextSize
            : 0;
        var chatClient = llmHost.CreateChatClient(llmEndpoint, modelId, contextSize);
        var agent = agentFactory.Create(chatClient);
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromMinutes(reviewRequestTimeoutMinutes), _timeProvider);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        IReadOnlyList<ReviewActionItem> items;
        try
        {
            items = await agent.ReviewAsync(diffText, agentInstructions, linkedSource.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && timeoutSource.IsCancellationRequested)
        {
            throw new TimeoutException($"The LLM review request exceeded the configured timeout of {reviewRequestTimeoutMinutes} minutes.", ex);
        }

        var orderedItems = items
            .OrderBy(item => item.Severity)
            .ThenBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.LineNumber)
            .ToArray();

        return new PullRequestReviewResult(pullRequestUrl, title, orderedItems);
    }

    private static void ValidateReviewRequestTimeout(int timeoutMinutes)
    {
        if (timeoutMinutes <= 0 || timeoutMinutes > MaximumReviewRequestTimeoutMinutes)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutMinutes), $"The review request timeout must be between 1 and {MaximumReviewRequestTimeoutMinutes} minutes.");
        }
    }
}
