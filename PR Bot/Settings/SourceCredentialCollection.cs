namespace PR_Bot.Settings;

public sealed class SourceCredentialCollection
{
    public Dictionary<Guid, SourceCredential> Entries { get; set; } = [];
}
