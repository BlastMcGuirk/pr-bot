namespace PRBot.Core.Models;

public sealed class PullRequestSourceConfiguration
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string SourceTypeId { get; set; } = string.Empty;

    public string SourceUrl { get; set; } = string.Empty;

    public SourceAuthenticationMethod AuthenticationMethod { get; set; }
}
