using PRBot.Core.Models;
using PRBot.Core.Abstractions;

namespace PRBot.Core.Sources;

public static class PullRequestSourceCatalog
{
    public const string AzureDevOpsId = "azure-devops";
    public const string BitbucketCloudId = "bitbucket-cloud";
    public const string BitbucketServerId = "bitbucket-server";

    private static readonly SourceAuthenticationMethod[] PatOnly = [SourceAuthenticationMethod.PersonalAccessToken];
    private static readonly SourceAuthenticationMethod[] PatAndUsernamePassword =
    [
        SourceAuthenticationMethod.PersonalAccessToken,
        SourceAuthenticationMethod.UsernamePassword
    ];

    public static IReadOnlyList<PullRequestSourceType> SourceTypes { get; } =
    [
        new(AzureDevOpsId, "Azure DevOps", "dev.azure.com", PatOnly,
            (client, host) => new AzureDevOpsPullRequestSource(client, host)),
        new(BitbucketCloudId, "Bitbucket Cloud", "bitbucket.org", PatAndUsernamePassword,
            (client, host) => new BitbucketPullRequestSource(client, host)),
        new(BitbucketServerId, "Bitbucket Server", "bitbucket.<server>.com", PatAndUsernamePassword,
            (client, host) => new BitbucketServerPullRequestSource(client, host))
    ];

    public static PullRequestSourceType? Find(string id) =>
        SourceTypes.FirstOrDefault(sourceType => sourceType.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static IPullRequestSource CreateSource(HttpClient httpClient, PullRequestSourceConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(configuration);
        var sourceType = Find(configuration.SourceTypeId)
                         ?? throw new NotSupportedException($"Source type '{configuration.SourceTypeId}' is not registered.");
        return sourceType.CreateSource(httpClient, GetHost(configuration.SourceUrl));
    }

    public static string GetHost(string sourceUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceUrl);
        var value = sourceUrl.Trim();
        if (value.Contains('<') || value.Contains('>'))
        {
            throw new ArgumentException("Replace the server placeholder with your Bitbucket Server domain.", nameof(sourceUrl));
        }

        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "https://" + value;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ArgumentException("Enter a valid source URL or domain.", nameof(sourceUrl));
        }

        return uri.IdnHost;
    }

    public static bool TryValidateUniqueHosts(
        IEnumerable<PullRequestSourceConfiguration> configurations,
        out string duplicateHost)
    {
        ArgumentNullException.ThrowIfNull(configurations);
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var configuration in configurations)
        {
            var host = GetHost(configuration.SourceUrl);
            if (!hosts.Add(host))
            {
                duplicateHost = host;
                return false;
            }
        }

        duplicateHost = string.Empty;
        return true;
    }
}
