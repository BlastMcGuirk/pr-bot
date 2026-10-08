namespace PR_Bot.Settings;

public interface ISourceCredentialStore
{
    Task<SourceCredentialCollection> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(SourceCredentialCollection credentials, CancellationToken cancellationToken = default);
}
