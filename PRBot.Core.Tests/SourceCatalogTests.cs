using PRBot.Core.Models;
using PRBot.Core.Sources;

namespace PRBot.Core.Tests;

public sealed class SourceCatalogTests
{
    [Fact]
    public void CatalogListsSourceTypesWithExpectedUrlDefaultsAndAuthenticationMethods()
    {
        var azureDevOps = PullRequestSourceCatalog.Find(PullRequestSourceCatalog.AzureDevOpsId);
        var bitbucketCloud = PullRequestSourceCatalog.Find(PullRequestSourceCatalog.BitbucketCloudId);
        var bitbucketServer = PullRequestSourceCatalog.Find(PullRequestSourceCatalog.BitbucketServerId);

        Assert.Equal("dev.azure.com", azureDevOps?.DefaultSourceUrl);
        Assert.Equal(SourceAuthenticationMethod.PersonalAccessToken, Assert.Single(azureDevOps!.AuthenticationMethods));
        Assert.Equal("bitbucket.org", bitbucketCloud?.DefaultSourceUrl);
        Assert.Contains(SourceAuthenticationMethod.UsernamePassword, bitbucketCloud!.AuthenticationMethods);
        Assert.Equal("bitbucket.<server>.com", bitbucketServer?.DefaultSourceUrl);
        Assert.Contains(SourceAuthenticationMethod.PersonalAccessToken, bitbucketServer!.AuthenticationMethods);
    }

    [Theory]
    [InlineData("bitbucket.org", "bitbucket.org")]
    [InlineData("https://Code.Example.com:8443/bitbucket", "code.example.com")]
    public void GetHostNormalizesSourceDomain(string sourceUrl, string expectedHost)
    {
        Assert.Equal(expectedHost, PullRequestSourceCatalog.GetHost(sourceUrl));
    }

    [Fact]
    public void GetHostRequiresBitbucketServerPlaceholderToBeReplaced()
    {
        Assert.Throws<ArgumentException>(() => PullRequestSourceCatalog.GetHost("bitbucket.<server>.com"));
    }

    [Fact]
    public void TryValidateUniqueHostsRejectsDuplicateSourceHostsCaseInsensitively()
    {
        PullRequestSourceConfiguration[] configurations =
        [
            new() { SourceTypeId = PullRequestSourceCatalog.BitbucketCloudId, SourceUrl = "bitbucket.org" },
            new() { SourceTypeId = PullRequestSourceCatalog.BitbucketServerId, SourceUrl = "https://BITBUCKET.ORG" }
        ];

        Assert.False(PullRequestSourceCatalog.TryValidateUniqueHosts(configurations, out var duplicateHost));
        Assert.Equal("bitbucket.org", duplicateHost);
    }

    [Fact]
    public void CreateSourceUsesConfiguredTypeAndHost()
    {
        using var httpClient = new HttpClient();
        var config = new PullRequestSourceConfiguration
        {
            SourceTypeId = PullRequestSourceCatalog.BitbucketServerId,
            SourceUrl = "https://stash.example.com",
            AuthenticationMethod = SourceAuthenticationMethod.UsernamePassword
        };

        var source = PullRequestSourceCatalog.CreateSource(httpClient, config);

        Assert.Equal(PullRequestSourceCatalog.BitbucketServerId, source.Id);
        Assert.True(source.CanHandle(new Uri("https://stash.example.com/projects/TEAM/repos/repo/pull-requests/5")));
        Assert.False(source.CanHandle(new Uri("https://other.example.com/projects/TEAM/repos/repo/pull-requests/5")));
    }
}
