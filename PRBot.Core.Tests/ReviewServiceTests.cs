using Microsoft.Extensions.AI;
using PRBot.Core.Abstractions;
using PRBot.Core.Models;
using PRBot.Core.Review;

namespace PRBot.Core.Tests;

public sealed class ReviewServiceTests
{
    [Fact]
    public async Task ReviewAsync_PassesOnlyFetchedDiffAndInstructionsAndOrdersFindingsBySeverity()
    {
        var pullRequestUrl = new Uri("https://test.local/pr/4");
        var diff = "only-this-diff-is-review-context";
        var instructions = "Project-specific review rules";
        var source = new FakeSource(diff);
        var agent = new RecordingAgent(
        [
            Item(ActionItemSeverity.Low, "low"),
            Item(ActionItemSeverity.Critical, "critical"),
            Item(ActionItemSeverity.High, "high")
        ]);
        var host = new FakeHost("ollama");
        var service = new PullRequestReviewService(
            [source],
            [host],
            new FakeAgentFactory(agent));

        var result = await service.ReviewAsync(new ReviewRequest(
            pullRequestUrl,
            "pat",
            instructions,
            "ollama",
            new Uri("http://localhost:1234/v1"),
            "model")
        {
            OllamaContextSize = 16384
        });

        Assert.Equal(diff, agent.ReceivedDiff);
        Assert.Equal(instructions, agent.ReceivedInstructions);
        Assert.Equal(pullRequestUrl, source.RequestedUrl);
        Assert.Equal(16384, host.ContextSize);
        Assert.Equal(new[] { "critical", "high", "low" }, result.ActionItems.Select(item => item.Title));
    }

    [Fact]
    public async Task ReviewAsync_RejectsUnregisteredPullRequestUrls()
    {
        var service = new PullRequestReviewService([], [new FakeHost()], new FakeAgentFactory(new RecordingAgent([])));

        await Assert.ThrowsAsync<NotSupportedException>(() => service.ReviewAsync(new ReviewRequest(
            new Uri("https://unknown.local/pr/4"),
            "pat",
            string.Empty,
            "test-host",
            new Uri("http://localhost:1234/v1"),
            "model")));
    }

    [Fact]
    public async Task ReviewDiffAsync_ReviewsSuppliedDiffWithoutPullRequestSource()
    {
        var diff = "supplied-local-diff";
        var instructions = "Project-specific review rules";
        var agent = new RecordingAgent(
        [
            Item(ActionItemSeverity.Low, "low"),
            Item(ActionItemSeverity.Critical, "critical")
        ]);
        var service = new PullRequestReviewService([], [new FakeHost()], new FakeAgentFactory(agent));

        var result = await service.ReviewDiffAsync(
            diff,
            instructions,
            "test-host",
            new Uri("http://localhost:1234/v1"),
            "model");

        Assert.Equal(diff, agent.ReceivedDiff);
        Assert.Equal(instructions, agent.ReceivedInstructions);
        Assert.Null(result.PullRequestUrl);
        Assert.Equal("Local diff review", result.PullRequestTitle);
        Assert.Equal(new[] { "critical", "low" }, result.ActionItems.Select(item => item.Title));
    }

    [Theory]
    [InlineData("ollama", 16384)]
    [InlineData("lm-studio", 0)]
    public async Task ReviewDiffAsync_PassesContextSizeOnlyToOllama(string hostId, int expectedContextSize)
    {
        var host = new FakeHost(hostId);
        var service = new PullRequestReviewService([], [host], new FakeAgentFactory(new RecordingAgent([])));

        await service.ReviewDiffAsync(
            "supplied-local-diff",
            string.Empty,
            hostId,
            new Uri("http://localhost:1234/v1"),
            "model",
            16384);

        Assert.Equal(expectedContextSize, host.ContextSize);
    }

    private static ReviewActionItem Item(ActionItemSeverity severity, string title) =>
        new(title, severity, "Description", "Why", "Suggested fix", "file.cs", 3);

    private sealed class FakeSource(string diff) : IPullRequestSource
    {
        public string Id => "fake";

        public Uri? RequestedUrl { get; private set; }

        public bool CanHandle(Uri pullRequestUrl) => pullRequestUrl.Host == "test.local";

        public Task<PullRequestDiff> FetchDiffAsync(Uri pullRequestUrl, string personalAccessToken, CancellationToken cancellationToken = default)
        {
            RequestedUrl = pullRequestUrl;
            return Task.FromResult(new PullRequestDiff(pullRequestUrl, "Test PR", diff));
        }
    }

    private sealed class FakeHost(string id = "test-host") : ILocalLlmHost
    {
        public string Id => id;
        public string DisplayName => "Test";
        public Uri DefaultEndpoint => new("http://localhost");
        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(Uri endpoint, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ModelInfo>>([]);
        public int? ContextSize { get; private set; }

        public IChatClient CreateChatClient(Uri endpoint, string modelId, int contextSize = 0)
        {
            ContextSize = contextSize;
            return null!;
        }
    }

    private sealed class FakeAgentFactory(IPrReviewAgent agent) : IPrReviewAgentFactory
    {
        public IPrReviewAgent Create(IChatClient chatClient) => agent;
    }

    private sealed class RecordingAgent(IReadOnlyList<ReviewActionItem> items) : IPrReviewAgent
    {
        public string? ReceivedDiff { get; private set; }
        public string? ReceivedInstructions { get; private set; }

        public Task<IReadOnlyList<ReviewActionItem>> ReviewAsync(string diffText, string agentInstructions, CancellationToken cancellationToken = default)
        {
            ReceivedDiff = diffText;
            ReceivedInstructions = agentInstructions;
            return Task.FromResult(items);
        }
    }
}
