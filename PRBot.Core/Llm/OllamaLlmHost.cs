using System.Net.Http.Json;
using System.Text.Json;
using OpenAI;
using System.ClientModel;
using Microsoft.Extensions.AI;
using PRBot.Core.Abstractions;
using PRBot.Core.Models;

namespace PRBot.Core.Llm;

public sealed class OllamaLlmHost(HttpClient httpClient) : ILocalLlmHost
{
    public string Id => "ollama";

    public string DisplayName => "Ollama";

    public Uri DefaultEndpoint => new("http://localhost:11434");

    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(
        Uri endpoint,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(Join(endpoint, "api/tags"), cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("models", out var models))
        {
            return [];
        }

        return models.EnumerateArray()
            .Select(model => model.TryGetProperty("name", out var name) ? name.GetString() : null)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => new ModelInfo(name!, name!))
            .ToArray();
    }

    public IChatClient CreateChatClient(Uri endpoint, string modelId, int contextSize = 0)
    {
        var client = new OpenAIClient(
            new ApiKeyCredential("ollama"),
            new OpenAIClientOptions { Endpoint = OpenAiEndpoint(endpoint) });
        var chatClient = client.GetChatClient(modelId).AsIChatClient();
        return contextSize > 0 ? new OllamaContextSizeChatClient(chatClient, contextSize) : chatClient;
    }

    private static Uri Join(Uri endpoint, string path)
    {
        var root = endpoint.AbsoluteUri.TrimEnd('/');
        if (root.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            root = root[..^3];
        }

        return new Uri($"{root}/{path}");
    }

    private static Uri OpenAiEndpoint(Uri endpoint)
    {
        var root = endpoint.AbsoluteUri.TrimEnd('/');
        return new Uri(root.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? root : $"{root}/v1");
    }
}

public sealed class OllamaContextSizeChatClient(IChatClient innerClient, int contextSize) : DelegatingChatClient(innerClient)
{
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(messages, AddContextSize(options), cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(messages, AddContextSize(options), cancellationToken);

    private ChatOptions AddContextSize(ChatOptions? options)
    {
        if (contextSize <= 0)
        {
            return options ?? new ChatOptions();
        }

        var requestOptions = options?.Clone() ?? new ChatOptions();
        requestOptions.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        requestOptions.AdditionalProperties["options"] = new Dictionary<string, object?>
        {
            ["num_ctx"] = contextSize
        };
        return requestOptions;
    }
}
