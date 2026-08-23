using System.Text.Json;
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

    /// <summary>
    /// Requests a strongly-typed JSON response via MEAI structured output, with the same
    /// retry/timeout envelope as <see cref="GetCompletionAsync"/>. If the model cannot honor the
    /// JSON schema (common with local Ollama models), it falls back to a plain-text call and
    /// deserializes the returned JSON, so callers get a <typeparamref name="T"/> either way.
    /// </summary>
    public static async Task<T> GetStructuredAsync<T>(
        IChatClient chatClient,
        string prompt,
        StructRAGConfig config,
        ILogger? logger = null,
        CancellationToken cancellationToken = default) where T : class
    {
        if (chatClient is null)
            throw new ArgumentNullException(nameof(chatClient));

        var messages = new List<ChatMessage> { new(ChatRole.User, prompt) };
        var options = new ChatOptions
        {
            Temperature = config.Temperature,
            MaxOutputTokens = config.MaxOutputTokens
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds));

        var retryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                config.MaxRetries,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (exception, delay, attempt, _) =>
                {
                    logger?.LogWarning(
                        "Structured LLM attempt {Attempt} failed ({Reason}), retrying in {Delay}s",
                        attempt,
                        exception?.Message ?? "empty result",
                        delay.TotalSeconds);
                });

        try
        {
            return await retryPolicy.ExecuteAsync(async () =>
            {
                var response = await chatClient
                    .GetResponseAsync<T>(messages, options, cancellationToken: timeout.Token)
                    .ConfigureAwait(false);
                return response.Result ?? throw new InvalidOperationException("Empty structured result");
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Structured output failed; falling back to text JSON parse");

            var text = await retryPolicy.ExecuteAsync(async () =>
            {
                var response = await chatClient.GetResponseAsync(messages, options, timeout.Token).ConfigureAwait(false);
                return response.Text ?? throw new InvalidOperationException("Empty response");
            }).ConfigureAwait(false);

            return JsonSerializer.Deserialize<T>(text, AIJsonUtilities.DefaultOptions)
                ?? throw new InvalidOperationException($"Failed to parse JSON into {typeof(T).Name}");
        }
    }
}
