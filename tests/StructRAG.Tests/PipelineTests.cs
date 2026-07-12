using Microsoft.Extensions.AI;
using NUnit.Framework;
using StructRAG.Pipeline;

namespace StructRAG.Tests;

[TestFixture]
internal sealed class PipelineTests
{
    [Test]
    public void Build_ReturnsNonNullWorkflow()
    {
        var workflow = StructRAGPipeline.Build(new FakeChatClient());

        Assert.That(workflow, Is.Not.Null);
    }
}

/// <summary>
/// Minimal IChatClient stub for tests that only need to compose the workflow,
/// not actually invoke LLM calls.
/// </summary>
internal sealed class FakeChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
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
