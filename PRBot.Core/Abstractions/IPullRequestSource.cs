using PRBot.Core.Models;

namespace PRBot.Core.Abstractions;

public interface IPullRequestSource
{
    string Id { get; }

    bool CanHandle(Uri pullRequestUrl);

    Task<PullRequestDiff> FetchDiffAsync(
        Uri pullRequestUrl,
        string personalAccessToken,
        CancellationToken cancellationToken = default);
}
