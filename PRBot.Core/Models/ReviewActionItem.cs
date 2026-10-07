namespace PRBot.Core.Models;

public enum ActionItemSeverity
{
    Critical,
    High,
    Medium,
    Low,
    Informational
}

public sealed record ReviewActionItem(
    string Title,
    ActionItemSeverity Severity,
    string Description,
    string WhyItMatters,
    string SuggestedFix,
    string? FilePath,
    int? LineNumber);
