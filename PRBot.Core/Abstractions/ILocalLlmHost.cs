using Microsoft.Extensions.AI;
using PRBot.Core.Models;

namespace PRBot.Core.Abstractions;

public interface ILocalLlmHost
{
    string Id { get; }

    string DisplayName { get; }

    Uri DefaultEndpoint { get; }

    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(Uri endpoint, CancellationToken cancellationToken = default);

    IChatClient CreateChatClient(Uri endpoint, string modelId, int contextSize = 0);
}
