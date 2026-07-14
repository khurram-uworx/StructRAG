using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using StructRAG.Pipeline;

namespace StructRAG.Tests;

[TestFixture]
internal sealed class PipelineTests
{
    [Test]
    public void Build_ReturnsNonNullWorkflow()
    {
        var workflow = StructRAGPipeline.Build(new FakeChatClient("chunk"), NullLoggerFactory.Instance);

        Assert.That(workflow, Is.Not.Null);
    }
}
