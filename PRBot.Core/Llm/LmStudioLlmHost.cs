using System.Text.Json;
using OpenAI;
using System.ClientModel;
using Microsoft.Extensions.AI;
using PRBot.Core.Abstractions;
using PRBot.Core.Models;

namespace PRBot.Core.Llm;

public sealed class LmStudioLlmHost(HttpClient httpClient) : ILocalLlmHost
{
    public string Id => "lm-studio";

    public string DisplayName => "LM Studio";

    public Uri DefaultEndpoint => new("http://localhost:1234/v1");

    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(
        Uri endpoint,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(Join(endpoint, "models"), cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("data", out var models))
        {
            return [];
        }

        return models.EnumerateArray()
            .Select(model => model.TryGetProperty("id", out var id) ? id.GetString() : null)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => new ModelInfo(id!, id!))
            .ToArray();
    }

    public IChatClient CreateChatClient(Uri endpoint, string modelId)
    {
        var client = new OpenAIClient(
            new ApiKeyCredential("lm-studio"),
            new OpenAIClientOptions { Endpoint = OpenAiEndpoint(endpoint) });
        return client.GetChatClient(modelId).AsIChatClient();
    }

    private static Uri Join(Uri endpoint, string path)
    {
        var root = endpoint.AbsoluteUri.TrimEnd('/');
        return new Uri($"{root}/{path}");
    }

    private static Uri OpenAiEndpoint(Uri endpoint)
    {
        var root = endpoint.AbsoluteUri.TrimEnd('/');
        return new Uri(root.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? root : $"{root}/v1");
    }
}
