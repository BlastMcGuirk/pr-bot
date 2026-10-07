using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using PRBot.Core.Abstractions;
using PRBot.Core.Models;

namespace PRBot.Core.Sources;

public sealed class BitbucketPullRequestSource(HttpClient httpClient) : IPullRequestSource
{
    private static readonly Regex CloudPullRequestPattern = new(
        @"^/(?<workspace>[^/]+)/(?<repository>[^/]+)/pull-requests/(?<id>\d+)/?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DataCenterPullRequestPattern = new(
        @"^/projects/(?<project>[^/]+)/repos/(?<repository>[^/]+)/pull-requests/(?<id>\d+)/?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Id => "bitbucket";

    public bool CanHandle(Uri pullRequestUrl) =>
        (pullRequestUrl.Host.Equals("bitbucket.org", StringComparison.OrdinalIgnoreCase)
         && CloudPullRequestPattern.IsMatch(pullRequestUrl.AbsolutePath))
        || DataCenterPullRequestPattern.IsMatch(pullRequestUrl.AbsolutePath);

    public async Task<PullRequestDiff> FetchDiffAsync(
        Uri pullRequestUrl,
        string personalAccessToken,
        CancellationToken cancellationToken = default)
    {
        if (!CanHandle(pullRequestUrl))
        {
            throw new ArgumentException("The URL is not a supported Bitbucket pull request URL.", nameof(pullRequestUrl));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(personalAccessToken);
        var isCloud = pullRequestUrl.Host.Equals("bitbucket.org", StringComparison.OrdinalIgnoreCase);
        var match = (isCloud ? CloudPullRequestPattern : DataCenterPullRequestPattern).Match(pullRequestUrl.AbsolutePath);
        var diffUrl = isCloud
            ? new Uri($"https://api.bitbucket.org/2.0/repositories/{match.Groups["workspace"].Value}/{match.Groups["repository"].Value}/pullrequests/{match.Groups["id"].Value}/diff")
            : new UriBuilder(pullRequestUrl)
            {
                Path = pullRequestUrl.AbsolutePath.TrimEnd('/') + ".diff",
                Query = string.Empty,
                Fragment = string.Empty
            }.Uri;

        using var request = new HttpRequestMessage(HttpMethod.Get, diffUrl);
        if (isCloud)
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"x-bitbucket-api-token-auth:{personalAccessToken}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
        else
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", personalAccessToken);
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var diff = await response.Content.ReadAsStringAsync(cancellationToken);
        var title = $"Pull request {match.Groups["id"].Value}";
        return new PullRequestDiff(pullRequestUrl, title, diff);
    }
}
