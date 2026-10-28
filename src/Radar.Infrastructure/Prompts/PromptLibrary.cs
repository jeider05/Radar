using System.Text.Json;
using System.Text.RegularExpressions;

namespace Radar.Infrastructure.Prompts;

public sealed class PromptException(string message) : Exception(message);

/// <summary>A parsed <c>.prompt.md</c> file: its system instructions and its user message template.</summary>
public sealed record PromptDefinition(string Name, int Version, string Description, string System, string UserTemplate);

public interface IPromptProvider
{
    /// <summary>Loads and parses <c>{name}.prompt.md</c>. Throws <see cref="PromptException"/> when the file is missing or malformed.</summary>
    PromptDefinition Load(string name);

    /// <summary>Loads the JSON response schema that accompanies a prompt, from <c>{name}.schema.json</c>.</summary>
    JsonElement LoadSchema(string name);

    /// <summary>Replaces every <c>{{variable}}</c> in <paramref name="template"/>. Throws <see cref="PromptException"/> when one is missing.</summary>
    string Render(string template, IReadOnlyDictionary<string, string> variables);
}

/// <summary>
/// Reads prompts from <c>.prompt.md</c>/<c>.schema.json</c> files instead of hardcoding them in C#.
/// Files are resolved from <see cref="DirectoryVariable"/> when set, otherwise from <c>AppContext.BaseDirectory/Prompts</c>.
/// </summary>
public sealed class PromptLibrary : IPromptProvider
{
    public const string DirectoryVariable = "RADAR_PROMPTS_DIR";

    private static readonly Regex VariablePattern = new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);

    private readonly string _baseDirectory;

    public PromptLibrary(string? baseDirectory = null)
    {
        _baseDirectory = string.IsNullOrWhiteSpace(baseDirectory) ? ResolveBaseDirectory() : baseDirectory;
    }

    private static string ResolveBaseDirectory()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(DirectoryVariable);
        return string.IsNullOrWhiteSpace(fromEnvironment) ? Path.Combine(AppContext.BaseDirectory, "Prompts") : fromEnvironment;
    }

    public PromptDefinition Load(string name)
    {
        var path = Path.Combine(_baseDirectory, $"{name}.prompt.md");
        if (!File.Exists(path))
        {
            throw new PromptException($"Prompt '{name}' was not found at '{path}'.");
        }

        return Parse(name, File.ReadAllText(path));
    }

    public JsonElement LoadSchema(string name)
    {
        var path = Path.Combine(_baseDirectory, $"{name}.schema.json");
        if (!File.Exists(path))
        {
            throw new PromptException($"Schema for prompt '{name}' was not found at '{path}'.");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    public string Render(string template, IReadOnlyDictionary<string, string> variables) =>
        VariablePattern.Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            return variables.TryGetValue(key, out var value)
                ? value
                : throw new PromptException($"Prompt variable '{{{{{key}}}}}' was not provided.");
        });

    private static PromptDefinition Parse(string name, string content)
    {
        var (frontMatter, body) = SplitFrontMatter(name, content);
        var (version, description) = ParseFrontMatter(name, frontMatter);

        var systemIndex = body.IndexOf("## System", StringComparison.Ordinal);
        var userIndex = body.IndexOf("## User", StringComparison.Ordinal);
        if (systemIndex < 0 || userIndex < 0 || userIndex < systemIndex)
        {
            throw new PromptException($"Prompt '{name}' must have a '## System' section followed by a '## User' section.");
        }

        var system = body[(systemIndex + "## System".Length)..userIndex].Trim();
        var user = body[(userIndex + "## User".Length)..].Trim();
        if (system.Length == 0 || user.Length == 0)
        {
            throw new PromptException($"Prompt '{name}' has an empty System or User section.");
        }

        return new PromptDefinition(name, version, description, system, user);
    }

    private static (string FrontMatter, string Body) SplitFrontMatter(string name, string content)
    {
        var trimmed = content.TrimStart('﻿', '\r', '\n');
        if (!trimmed.StartsWith("---", StringComparison.Ordinal))
        {
            throw new PromptException($"Prompt '{name}' must start with a '---' YAML header.");
        }

        var closingIndex = trimmed.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (closingIndex < 0)
        {
            throw new PromptException($"Prompt '{name}' has no closing '---' for its YAML header.");
        }

        return (trimmed[3..closingIndex], trimmed[(closingIndex + "\n---".Length)..]);
    }

    private static (int Version, string Description) ParseFrontMatter(string name, string frontMatter)
    {
        string? promptName = null;
        int? version = null;
        string? description = null;

        foreach (var line in frontMatter.Split('\n'))
        {
            var trimmedLine = line.Trim();
            var separator = trimmedLine.IndexOf(':');
            if (separator < 0)
            {
                continue;
            }

            var key = trimmedLine[..separator].Trim();
            var value = trimmedLine[(separator + 1)..].Trim();
            switch (key)
            {
                case "name":
                    promptName = value;
                    break;
                case "version" when int.TryParse(value, out var parsed):
                    version = parsed;
                    break;
                case "description":
                    description = value;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(promptName) || version is null || string.IsNullOrWhiteSpace(description))
        {
            throw new PromptException($"Prompt '{name}' is missing 'name', 'version' or 'description' in its YAML header.");
        }

        return (version.Value, description);
    }
}
