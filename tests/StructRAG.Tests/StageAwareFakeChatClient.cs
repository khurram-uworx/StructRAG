using Microsoft.Extensions.AI;

namespace StructRAG.Tests;

/// <summary>
/// IChatClient that returns different canned responses based on which prompt is being called.
/// Detects the stage by checking for stage-specific prompt markers.
/// </summary>
internal sealed class StageAwareFakeChatClient : IChatClient
{
    readonly Func<string, string> responseMap;

    public StageAwareFakeChatClient(Func<string, string> responseMap)
    {
        this.responseMap = responseMap;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var prompt = messages.Last().Text ?? string.Empty;
        var response = responseMap(prompt);
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public object? GetService(Type serviceType, object? key = null) => null;

    public TService? GetService<TService>(object? key = null) where TService : class => null;

    public void Dispose() { }

    internal static StageAwareFakeChatClient ForRoute(string routeResponse)
    {
        return new StageAwareFakeChatClient(prompt => routeResponse);
    }

    internal static StageAwareFakeChatClient ForConstruct(string constructResponse)
    {
        return new StageAwareFakeChatClient(prompt => constructResponse);
    }

    internal static StageAwareFakeChatClient ForDecompose(string decomposeResponse)
    {
        return new StageAwareFakeChatClient(prompt => decomposeResponse);
    }

    internal static StageAwareFakeChatClient ForExtract(string extractResponse)
    {
        return new StageAwareFakeChatClient(prompt => extractResponse);
    }

    internal static StageAwareFakeChatClient ForMerge(string mergeResponse)
    {
        return new StageAwareFakeChatClient(prompt => mergeResponse);
    }

    internal static StageAwareFakeChatClient Full(
        string route, string construct, string decompose, string extract, string merge)
    {
        return new StageAwareFakeChatClient(prompt =>
        {
            if (prompt.Contains("determine which type")) return route;
            if (prompt.Contains("raw_content")) return construct;
            if (prompt.Contains("kb_info")) return decompose;
            if (prompt.Contains("Answer the Query based on the given Document")) return extract;
            return merge;
        });
    }
}
