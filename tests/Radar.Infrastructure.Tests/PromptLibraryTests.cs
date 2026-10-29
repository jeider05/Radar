using Radar.Infrastructure.Prompts;

namespace Radar.Infrastructure.Tests;

public sealed class PromptLibraryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Radar.Tests.Prompts", Guid.NewGuid().ToString("N"));

    public PromptLibraryTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void Load_parses_header_system_and_user_sections()
    {
        WritePrompt("greeting", """
            ---
            name: greeting
            version: 2
            description: Saluda a alguien.
            ---

            ## System

            Eres cordial.

            ## User

            Hola {{name}}, hoy es {{date}}.
            """);

        var prompt = new PromptLibrary(_directory).Load("greeting");

        Assert.Equal("greeting", prompt.Name);
        Assert.Equal(2, prompt.Version);
        Assert.Equal("Saluda a alguien.", prompt.Description);
        Assert.Equal("Eres cordial.", prompt.System);
        Assert.Equal("Hola {{name}}, hoy es {{date}}.", prompt.UserTemplate);
    }

    [Fact]
    public void Load_reads_prompts_from_subdirectories_by_relative_name()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "Novedades"));
        WritePrompt("Novedades/release-summary", """
            ---
            name: release-summary
            version: 1
            description: Resume un release.
            ---

            ## System
            Sistema.

            ## User
            Usuario {{title}}.
            """);

        var prompt = new PromptLibrary(_directory).Load("Novedades/release-summary");

        Assert.Equal("Novedades/release-summary", prompt.Name);
        Assert.Contains("{{title}}", prompt.UserTemplate);
    }

    [Fact]
    public void Load_throws_a_clear_error_when_the_file_is_missing()
    {
        var ex = Assert.Throws<PromptException>(() => new PromptLibrary(_directory).Load("missing"));

        Assert.Contains("missing", ex.Message);
    }

    [Theory]
    [InlineData("""
        ---
        name: broken
        description: Sin version.
        ---
        ## System
        S
        ## User
        U
        """)]
    [InlineData("""
        ---
        name: broken
        version: 1
        ---
        ## System
        S
        ## User
        U
        """)]
    public void Load_throws_when_the_yaml_header_is_incomplete(string content)
    {
        WritePrompt("broken", content);

        Assert.Throws<PromptException>(() => new PromptLibrary(_directory).Load("broken"));
    }

    [Fact]
    public void Load_throws_when_the_user_section_is_missing()
    {
        WritePrompt("no-user", """
            ---
            name: no-user
            version: 1
            description: Sin sección User.
            ---

            ## System
            Solo tengo sistema.
            """);

        Assert.Throws<PromptException>(() => new PromptLibrary(_directory).Load("no-user"));
    }

    [Fact]
    public void Render_replaces_every_variable()
    {
        var text = new PromptLibrary(_directory).Render(
            "Proyecto {{projectName}}, release {{title}}.",
            new Dictionary<string, string> { ["projectName"] = "jqlang/jq", ["title"] = "1.8.2" });

        Assert.Equal("Proyecto jqlang/jq, release 1.8.2.", text);
    }

    [Fact]
    public void Render_throws_a_clear_error_when_a_variable_is_missing()
    {
        var ex = Assert.Throws<PromptException>(() => new PromptLibrary(_directory).Render(
            "Hola {{name}}.", new Dictionary<string, string>()));

        Assert.Contains("name", ex.Message);
    }

    [Fact]
    public void LoadSchema_parses_the_sibling_schema_file()
    {
        File.WriteAllText(
            Path.Combine(_directory, "release-summary.schema.json"),
            """{"type":"OBJECT","properties":{"puntos":{"type":"ARRAY"}}}""");

        var schema = new PromptLibrary(_directory).LoadSchema("release-summary");

        Assert.Equal("OBJECT", schema.GetProperty("type").GetString());
    }

    [Fact]
    public void LoadSchema_throws_a_clear_error_when_the_file_is_missing()
    {
        var ex = Assert.Throws<PromptException>(() => new PromptLibrary(_directory).LoadSchema("missing"));

        Assert.Contains("missing", ex.Message);
    }

    [Fact]
    public void The_release_detail_prompt_loads_with_its_variables_and_schema()
    {
        var library = new PromptLibrary();

        var prompt = library.Load("Detalle/release-detail");
        var rendered = library.Render(prompt.UserTemplate, new Dictionary<string, string>
        {
            ["projectName"] = "org/repo",
            ["title"] = "v1.0.0",
            ["publishedAt"] = "2026-10-27",
            ["url"] = "https://example.invalid",
            ["userOs"] = "Windows",
            ["userShell"] = "PowerShell",
            ["notes"] = "Notas del release.",
        });
        var schema = library.LoadSchema("Detalle/release-detail");

        Assert.True(prompt.Version >= 1);
        Assert.Contains("JSON", prompt.System);
        Assert.Contains("Windows", rendered);
        Assert.Contains("PowerShell", rendered);
        Assert.Equal("OBJECT", schema.GetProperty("type").GetString());
        Assert.True(schema.GetProperty("properties").TryGetProperty("resumen", out _));
    }

    [Fact]
    public void The_moved_release_explanation_prompt_matches_the_original_system_instruction()
    {
        const string originalSystemInstruction = """
            Eres un asistente que explica novedades de software a desarrolladores, con un tono educativo y claro.
            Responde siempre en español.
            Usa únicamente la información de las notas del release que te proporcionan. No inventes funcionalidades,
            cifras, fechas, versiones ni consecuencias que no aparezcan en las notas. Si algo no está claro en las notas,
            dilo explícitamente en lugar de suponerlo.
            Estructura la respuesta en texto plano, sin Markdown, con estas secciones:
            Resumen: dos o tres frases sobre qué trae esta versión.
            Novedades principales: una lista breve con guiones.
            Por qué importa: qué significa para quien usa este proyecto, solo en la medida en que las notas lo permitan.
            Sé conciso: no más de 350 palabras.
            """;

        var prompt = new PromptLibrary().Load("Detalle/release-explanation");

        Assert.Equal(originalSystemInstruction.Trim(), prompt.System);
    }

    private void WritePrompt(string relativeName, string content)
    {
        var path = Path.Combine(_directory, $"{relativeName}.prompt.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
