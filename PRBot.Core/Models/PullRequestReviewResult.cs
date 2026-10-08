namespace PRBot.Core.Models;

public sealed record PullRequestReviewResult(
    Uri? PullRequestUrl,
    string PullRequestTitle,
    IReadOnlyList<ReviewActionItem> ActionItems);

public sealed record ModelInfo(string Id, string DisplayName);
