using System.Reflection;

namespace StructRAG.Stages;

/// <summary>
/// Loads embedded prompt resources from the StructRAG assembly.
/// </summary>
internal static class PromptLoader
{
    static readonly Assembly cachedAssembly = typeof(PromptLoader).Assembly;

    /// <summary>
    /// Loads a prompt from the embedded resource at Prompts/StructRAG/{name}.txt
    /// and performs variable substitution.
    /// </summary>
    public static string Load(string name, IReadOnlyDictionary<string, string> variables)
    {
        var resourceName = $"StructRAG.Prompts.StructRAG.{name}.txt";
        using var stream = cachedAssembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded prompt not found: {resourceName}");

        using var reader = new StreamReader(stream);
        var prompt = reader.ReadToEnd();

        foreach (var (key, value) in variables)
        {
            prompt = prompt.Replace($"{{{{${key}}}}}", value);
        }

        return prompt;
    }
}
