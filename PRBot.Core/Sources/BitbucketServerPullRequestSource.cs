using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using PRBot.Core.Abstractions;
using PRBot.Core.Models;

namespace PRBot.Core.Sources;

public sealed class BitbucketServerPullRequestSource(HttpClient httpClient, string sourceHost) : IPullRequestSource
{
    private static readonly Regex PullRequestPattern = new(
        @"^/projects/(?<project>[^/]+)/repos/(?<repository>[^/]+)/pull-requests/(?<id>\d+)/?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Id => "bitbucket-server";

    public bool CanHandle(Uri pullRequestUrl) =>
        pullRequestUrl.Host.Equals(sourceHost, StringComparison.OrdinalIgnoreCase)
        && PullRequestPattern.IsMatch(pullRequestUrl.AbsolutePath);

    public Task<PullRequestDiff> FetchDiffAsync(
        Uri pullRequestUrl,
        string personalAccessToken,
        CancellationToken cancellationToken = default) =>
        FetchDiffAsync(pullRequestUrl, SourceAuthenticationMethod.PersonalAccessToken, string.Empty, personalAccessToken, cancellationToken);

    public async Task<PullRequestDiff> FetchDiffAsync(
        Uri pullRequestUrl,
        SourceAuthenticationMethod authenticationMethod,
        string username,
        string credential,
        CancellationToken cancellationToken = default)
    {
        if (!CanHandle(pullRequestUrl))
        {
            throw new ArgumentException("The URL is not a supported Bitbucket Server pull request URL.", nameof(pullRequestUrl));
        }

        var match = PullRequestPattern.Match(pullRequestUrl.AbsolutePath);
        var diffUrl = new UriBuilder(pullRequestUrl)
        {
            Path = pullRequestUrl.AbsolutePath.TrimEnd('/') + ".diff",
            Query = string.Empty,
            Fragment = string.Empty
        }.Uri;

        using var request = new HttpRequestMessage(HttpMethod.Get, diffUrl);
        if (authenticationMethod == SourceAuthenticationMethod.PersonalAccessToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(credential);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        }
        else if (authenticationMethod == SourceAuthenticationMethod.UsernamePassword)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(username);
            ArgumentException.ThrowIfNullOrWhiteSpace(credential);
            var basicCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{credential}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicCredentials);
        }
        else
        {
            throw new NotSupportedException($"Bitbucket Server does not support {authenticationMethod} authentication.");
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var diff = await response.Content.ReadAsStringAsync(cancellationToken);
        return new PullRequestDiff(pullRequestUrl, $"Pull request {match.Groups["id"].Value}", diff);
    }
}
