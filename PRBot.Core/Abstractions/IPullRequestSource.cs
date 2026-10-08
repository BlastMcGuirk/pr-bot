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

    async Task<PullRequestDiff> FetchDiffAsync(
        Uri pullRequestUrl,
        SourceAuthenticationMethod authenticationMethod,
        string username,
        string credential,
        CancellationToken cancellationToken = default)
    {
        if (authenticationMethod != SourceAuthenticationMethod.PersonalAccessToken)
        {
            throw new NotSupportedException($"Source '{Id}' does not support {authenticationMethod} authentication.");
        }

        return await FetchDiffAsync(pullRequestUrl, credential, cancellationToken);
    }
}
