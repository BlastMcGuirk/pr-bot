using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using PRBot.Core.Abstractions;
using PRBot.Core.Models;

namespace PRBot.Core.Sources;

public sealed class BitbucketPullRequestSource(HttpClient httpClient, string sourceHost = "bitbucket.org") : IPullRequestSource
{
    private static readonly Regex CloudPullRequestPattern = new(
        @"^/(?<workspace>[^/]+)/(?<repository>[^/]+)/pull-requests/(?<id>\d+)/?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public string Id => "bitbucket-cloud";

    public bool CanHandle(Uri pullRequestUrl) =>
        pullRequestUrl.Host.Equals(sourceHost, StringComparison.OrdinalIgnoreCase)
        && CloudPullRequestPattern.IsMatch(pullRequestUrl.AbsolutePath);

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
            throw new ArgumentException("The URL is not a supported Bitbucket pull request URL.", nameof(pullRequestUrl));
        }

        var match = CloudPullRequestPattern.Match(pullRequestUrl.AbsolutePath);
        var diffUrl = new Uri($"https://api.bitbucket.org/2.0/repositories/{match.Groups["workspace"].Value}/{match.Groups["repository"].Value}/pullrequests/{match.Groups["id"].Value}/diff");

        using var request = new HttpRequestMessage(HttpMethod.Get, diffUrl);
        if (authenticationMethod == SourceAuthenticationMethod.PersonalAccessToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(credential);
            var tokenCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"x-bitbucket-api-token-auth:{credential}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", tokenCredentials);
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
            throw new NotSupportedException($"Bitbucket Cloud does not support {authenticationMethod} authentication.");
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var diff = await response.Content.ReadAsStringAsync(cancellationToken);
        var title = $"Pull request {match.Groups["id"].Value}";
        return new PullRequestDiff(pullRequestUrl, title, diff);
    }
}
