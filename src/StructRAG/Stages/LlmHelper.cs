using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Polly;
using StructRAG.Models;

namespace StructRAG.Stages;

/// <summary>
/// Shared helper for making LLM completion calls across all pipeline executors.
/// Includes retry with exponential backoff for transient failures and a per-call timeout
/// so a single runaway LLM call cannot hang the pipeline.
/// </summary>
internal static class LlmHelper
{
    public static async Task<string> GetCompletionAsync(
        IChatClient chatClient,
        string prompt,
        StructRAGConfig config,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        if (chatClient is null)
            throw new ArgumentNullException(nameof(chatClient));

        var messages = new List<ChatMessage> { new(ChatRole.User, prompt) };
        var options = new ChatOptions
        {
            Temperature = config.Temperature,
            MaxOutputTokens = config.MaxOutputTokens
        };

        // Bound the call by a per-call timeout, while still honoring external cancellation.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds));

        var retryPolicy = Policy
            .Handle<HttpRequestException>()
            .OrResult<string>(string.IsNullOrEmpty)
            .WaitAndRetryAsync(
                config.MaxRetries,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, delay, attempt, _) =>
                {
                    logger?.LogWarning(
                        "LLM call attempt {Attempt} failed ({Reason}), retrying in {Delay}s",
                        attempt,
                        outcome.Exception?.Message ?? "empty response",
                        delay.TotalSeconds);
                });

        var result = await retryPolicy.ExecuteAsync(async () =>
        {
            var response = await chatClient.GetResponseAsync(messages, options, timeout.Token).ConfigureAwait(false);
            return response.Text ?? string.Empty;
        }).ConfigureAwait(false);

        return result;
    }
}
