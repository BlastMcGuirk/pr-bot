using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using PRBot.Core.Llm;

namespace PRBot.Core.Tests;

public sealed class OllamaContextSizeChatClientTests
{
    [Fact]
    public async Task GetResponseAsync_AddsNumCtxWithoutMutatingProvidedOptions()
    {
        var innerClient = new CapturingChatClient();
        var client = new OllamaContextSizeChatClient(innerClient, 16384);
        var options = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["existing"] = "preserved"
            }
        };

        await client.GetResponseAsync([], options);

        Assert.NotNull(innerClient.ReceivedOptions);
        Assert.Equal("preserved", innerClient.ReceivedOptions.AdditionalProperties!["existing"]);
        var ollamaOptions = Assert.IsType<Dictionary<string, object?>>(innerClient.ReceivedOptions.AdditionalProperties["options"]);
        Assert.Equal(16384, ollamaOptions["num_ctx"]);
        Assert.DoesNotContain("options", options.AdditionalProperties.Keys);
    }

    [Fact]
    public async Task GetResponseAsync_LeavesNumCtxUnsetWhenUsingHostDefault()
    {
        var innerClient = new CapturingChatClient();
        var client = new OllamaContextSizeChatClient(innerClient, 0);

        await client.GetResponseAsync([]);

        Assert.NotNull(innerClient.ReceivedOptions);
        Assert.Null(innerClient.ReceivedOptions.AdditionalProperties);
    }

    private sealed class CapturingChatClient : IChatClient
    {
        public ChatOptions? ReceivedOptions { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ReceivedOptions = options;
            return Task.FromResult<ChatResponse>(null!);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ReceivedOptions = options;
            return EmptyResponseUpdates();
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }

        private static async IAsyncEnumerable<ChatResponseUpdate> EmptyResponseUpdates(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
