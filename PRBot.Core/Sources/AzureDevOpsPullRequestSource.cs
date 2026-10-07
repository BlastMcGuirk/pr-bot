using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using PRBot.Core.Abstractions;
using PRBot.Core.Models;

namespace PRBot.Core.Sources;

public sealed class AzureDevOpsPullRequestSource(HttpClient httpClient) : IPullRequestSource
{
    private static readonly Regex PullRequestPattern = new(
        @"/(?<project>[^/]+)/_git/(?<repository>[^/]+)/pullrequest/(?<id>\d+)(?:/|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Id => "azure-devops";

    public bool CanHandle(Uri pullRequestUrl) =>
        (pullRequestUrl.Host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase)
         || pullRequestUrl.Host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase))
        && PullRequestPattern.IsMatch(pullRequestUrl.AbsolutePath);

    public async Task<PullRequestDiff> FetchDiffAsync(
        Uri pullRequestUrl,
        string personalAccessToken,
        CancellationToken cancellationToken = default)
    {
        if (!CanHandle(pullRequestUrl))
        {
            throw new ArgumentException("The URL is not an Azure DevOps pull request URL.", nameof(pullRequestUrl));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(personalAccessToken);
        var match = PullRequestPattern.Match(pullRequestUrl.AbsolutePath);
        var diffUrl = CreateDiffUrl(pullRequestUrl, match);
        using var request = new HttpRequestMessage(HttpMethod.Get, diffUrl);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($":{personalAccessToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var diff = await response.Content.ReadAsStringAsync(cancellationToken);
        return new PullRequestDiff(pullRequestUrl, $"Pull request {match.Groups["id"].Value}", diff);
    }

    private static Uri CreateDiffUrl(Uri pullRequestUrl, Match match)
    {
        var path = pullRequestUrl.AbsolutePath[..match.Index]
                   + match.Value.TrimEnd('/')
                   + ".diff";
        return new UriBuilder(pullRequestUrl) { Path = path, Query = string.Empty, Fragment = string.Empty }.Uri;
    }
}
