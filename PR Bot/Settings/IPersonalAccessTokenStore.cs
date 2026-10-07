namespace PR_Bot.Settings;

public interface IPersonalAccessTokenStore
{
    Task<PersonalAccessTokens> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(PersonalAccessTokens tokens, CancellationToken cancellationToken = default);
}
