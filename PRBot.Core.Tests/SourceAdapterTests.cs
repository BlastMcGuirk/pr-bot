using System.Net;
using System.Net.Http.Headers;
using System.Text;
using PRBot.Core.Models;
using PRBot.Core.Sources;

namespace PRBot.Core.Tests;

public sealed class SourceAdapterTests
{
    [Theory]
    [InlineData("https://dev.azure.com/acme/Project/_git/widgets/pullrequest/42", true)]
    [InlineData("https://acme.visualstudio.com/Project/_git/widgets/pullrequest/7", true)]
    [InlineData("https://example.com/Project/_git/widgets/pullrequest/42", false)]
    public void AzureDevOpsSource_RecognizesSupportedUrls(string value, bool expected)
    {
        var source = new AzureDevOpsPullRequestSource(new HttpClient(new CallbackHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))));

        Assert.Equal(expected, source.CanHandle(new Uri(value)));
    }

    [Fact]
    public async Task AzureDevOpsSource_FetchesOnlyDiffWithBasicPatAuthentication()
    {
        Uri? requestedUri = null;
        AuthenticationHeaderValue? authorization = null;
        var source = new AzureDevOpsPullRequestSource(new HttpClient(new CallbackHandler(request =>
        {
            requestedUri = request.RequestUri;
            authorization = request.Headers.Authorization;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("--- a/file.cs\n+++ b/file.cs") };
        })));
        var pullRequestUrl = new Uri("https://dev.azure.com/acme/Project/_git/widgets/pullrequest/42?view=files");

        var result = await source.FetchDiffAsync(pullRequestUrl, "secret-token");

        Assert.Equal("https://dev.azure.com/acme/Project/_git/widgets/pullrequest/42.diff", requestedUri?.AbsoluteUri);
        Assert.Equal("Basic", authorization?.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes(":secret-token")), authorization?.Parameter);
        Assert.Equal("--- a/file.cs\n+++ b/file.cs", result.DiffText);
    }

    [Fact]
    public async Task BitbucketCloudSource_UsesDiffApiAndApiTokenAuthentication()
    {
        Uri? requestedUri = null;
        AuthenticationHeaderValue? authorization = null;
        var source = new BitbucketPullRequestSource(new HttpClient(new CallbackHandler(request =>
        {
            requestedUri = request.RequestUri;
            authorization = request.Headers.Authorization;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("diff body") };
        })));
        var pullRequestUrl = new Uri("https://bitbucket.org/team/widgets/pull-requests/17/");

        var result = await source.FetchDiffAsync(pullRequestUrl, "bb-token");

        Assert.Equal("https://api.bitbucket.org/2.0/repositories/team/widgets/pullrequests/17/diff", requestedUri?.AbsoluteUri);
        Assert.Equal("Basic", authorization?.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("x-bitbucket-api-token-auth:bb-token")), authorization?.Parameter);
        Assert.Equal("diff body", result.DiffText);
    }

    [Fact]
    public async Task BitbucketCloudSource_UsesBasicUsernamePasswordAuthentication()
    {
        AuthenticationHeaderValue? authorization = null;
        var source = new BitbucketPullRequestSource(new HttpClient(new CallbackHandler(request =>
        {
            authorization = request.Headers.Authorization;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("diff body") };
        })));

        await source.FetchDiffAsync(
            new Uri("https://bitbucket.org/team/widgets/pull-requests/17"),
            SourceAuthenticationMethod.UsernamePassword,
            "alice",
            "app-password");

        Assert.Equal("Basic", authorization?.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:app-password")), authorization?.Parameter);
    }

    [Fact]
    public async Task BitbucketServerSource_UsesConfiguredHostAndSupportsBothAuthMethods()
    {
        var authorizationHeaders = new List<AuthenticationHeaderValue?>();
        var source = new BitbucketServerPullRequestSource(new HttpClient(new CallbackHandler(request =>
        {
            authorizationHeaders.Add(request.Headers.Authorization);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("diff body") };
        })), "stash.example.com");
        var pullRequestUrl = new Uri("https://stash.example.com/projects/TEAM/repos/widgets/pull-requests/7");

        Assert.True(source.CanHandle(pullRequestUrl));
        Assert.False(source.CanHandle(new Uri("https://other.example.com/projects/TEAM/repos/widgets/pull-requests/7")));
        await source.FetchDiffAsync(pullRequestUrl, SourceAuthenticationMethod.PersonalAccessToken, string.Empty, "server-pat");
        await source.FetchDiffAsync(pullRequestUrl, SourceAuthenticationMethod.UsernamePassword, "alice", "password");

        Assert.Equal("Bearer", authorizationHeaders[0]?.Scheme);
        Assert.Equal("server-pat", authorizationHeaders[0]?.Parameter);
        Assert.Equal("Basic", authorizationHeaders[1]?.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:password")), authorizationHeaders[1]?.Parameter);
    }

    [Fact]
    public async Task SourceHonorsCancellation()
    {
        var source = new AzureDevOpsPullRequestSource(new HttpClient(new CallbackHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.FetchDiffAsync(
            new Uri("https://dev.azure.com/acme/Project/_git/widgets/pullrequest/42"),
            "token",
            cancellation.Token));
    }

    private sealed class CallbackHandler(Func<HttpRequestMessage, HttpResponseMessage> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(callback(request));
    }
}
