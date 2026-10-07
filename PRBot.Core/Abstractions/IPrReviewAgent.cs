using Microsoft.Extensions.AI;
using PRBot.Core.Models;

namespace PRBot.Core.Abstractions;

public interface IPrReviewAgent
{
    Task<IReadOnlyList<ReviewActionItem>> ReviewAsync(
        string diffText,
        string agentInstructions,
        CancellationToken cancellationToken = default);
}

public interface IPrReviewAgentFactory
{
    IPrReviewAgent Create(IChatClient chatClient);
}
