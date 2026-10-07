namespace PRBot.Core.Models;

public sealed record PullRequestDiff(Uri PullRequestUrl, string Title, string DiffText);
