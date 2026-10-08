using PRBot.Core.Models;
using PRBot.Core.Abstractions;

namespace PRBot.Core.Sources;

public sealed record PullRequestSourceType(
    string Id,
    string DisplayName,
    string DefaultSourceUrl,
    IReadOnlyList<SourceAuthenticationMethod> AuthenticationMethods,
    Func<HttpClient, string, IPullRequestSource> CreateSource);
