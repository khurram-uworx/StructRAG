using Microsoft.Extensions.AI;

namespace StructRAG.Tests;

/// <summary>
/// Flexible IChatClient stub that returns canned responses.
/// Pass a function to control responses based on the prompt content.
/// </summary>
internal sealed class FakeChatClient(Func<string> responseFactory) : IChatClient
{
    public FakeChatClient(string response) : this(() => response) { }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseFactory())));
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
}
