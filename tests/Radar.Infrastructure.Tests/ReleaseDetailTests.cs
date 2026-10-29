using Radar.Core.Contents;

namespace Radar.Infrastructure.Tests;

public sealed class ReleaseDetailTests
{
    private const string ValidJson = """
        {
          "resumen": "Unifica modelos y perfiles en una pantalla y agrega música opcional.",
          "teAfecta": { "nivel": "si", "texto": "Afecta a quienes usan Gentle Shell." },
          "cambios": [
            { "tipo": "nuevo", "texto": "Modelos y perfiles en una sola pantalla." },
            { "tipo": "importante", "texto": "Cambia el formato de configuración." }
          ],
          "instalacion": [
            { "comando": "npm install -g gentle-pi@4.5.0", "opcional": false, "deducido": true },
            { "comando": "gentle-shell update --latest", "opcional": true, "deducido": false }
          ],
          "planDeUso": ["Abre /gentle:profiles.", "Revisa tus perfiles."],
          "versiones": [
            { "etiqueta": "Antes", "valor": "v4.4.0" },
            { "etiqueta": "Ahora", "valor": "v4.5.0" }
          ]
        }
        """;

    [Fact]
    public void TryParse_parses_a_full_valid_ficha()
    {
        var ok = ReleaseDetail.TryParse(ValidJson, out var detail, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal("Unifica modelos y perfiles en una pantalla y agrega música opcional.", detail!.Resumen);
        Assert.Equal(AffectsLevel.Si, detail.TeAfecta.Nivel);
        Assert.Equal(2, detail.Cambios.Count);
        Assert.Equal(ReleaseChangeType.Nuevo, detail.Cambios[0].Tipo);
        Assert.Equal(ReleaseChangeType.Importante, detail.Cambios[1].Tipo);
        Assert.Equal(2, detail.Instalacion.Count);
        Assert.False(detail.Instalacion[0].Opcional);
        Assert.True(detail.Instalacion[0].Deducido);
        Assert.True(detail.Instalacion[1].Opcional);
        Assert.Equal(["Abre /gentle:profiles.", "Revisa tus perfiles."], detail.PlanDeUso);
        Assert.Equal("v4.4.0", detail.VersionAnterior);
        Assert.Equal("v4.5.0", detail.VersionNueva);
    }

    [Fact]
    public void TryParse_allows_empty_optional_sections()
    {
        const string json = """
            {
              "resumen": "Corrige errores menores.",
              "teAfecta": { "nivel": "no", "texto": "No afecta a la mayoría." },
              "cambios": [],
              "instalacion": [],
              "planDeUso": [],
              "versiones": []
            }
            """;

        var ok = ReleaseDetail.TryParse(json, out var detail, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Empty(detail!.Cambios);
        Assert.Empty(detail.Instalacion);
        Assert.Empty(detail.PlanDeUso);
        Assert.Null(detail.VersionAnterior);
        Assert.Null(detail.VersionNueva);
    }

    [Fact]
    public void TryParse_rejects_a_resumen_longer_than_220_characters()
    {
        var json = $$"""
            {
              "resumen": "{{new string('x', 221)}}",
              "teAfecta": { "nivel": "no", "texto": "x" },
              "cambios": [], "instalacion": [], "planDeUso": [], "versiones": []
            }
            """;

        var ok = ReleaseDetail.TryParse(json, out var detail, out var error);

        Assert.False(ok);
        Assert.Null(detail);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParse_rejects_an_invalid_affects_level()
    {
        const string json = """
            {
              "resumen": "x",
              "teAfecta": { "nivel": "tal vez", "texto": "x" },
              "cambios": [], "instalacion": [], "planDeUso": [], "versiones": []
            }
            """;

        Assert.False(ReleaseDetail.TryParse(json, out _, out _));
    }

    [Fact]
    public void TryParse_rejects_a_change_with_an_invalid_tipo()
    {
        const string json = """
            {
              "resumen": "x",
              "teAfecta": { "nivel": "no", "texto": "x" },
              "cambios": [{ "tipo": "sorpresa", "texto": "x" }],
              "instalacion": [], "planDeUso": [], "versiones": []
            }
            """;

        Assert.False(ReleaseDetail.TryParse(json, out _, out _));
    }

    [Fact]
    public void TryParse_rejects_an_install_command_with_empty_comando()
    {
        const string json = """
            {
              "resumen": "x",
              "teAfecta": { "nivel": "no", "texto": "x" },
              "cambios": [],
              "instalacion": [{ "comando": "", "opcional": false, "deducido": false }],
              "planDeUso": [], "versiones": []
            }
            """;

        Assert.False(ReleaseDetail.TryParse(json, out _, out _));
    }

    [Fact]
    public void TryParse_rejects_invalid_json()
    {
        var ok = ReleaseDetail.TryParse("no es json", out var detail, out var error);

        Assert.False(ok);
        Assert.Null(detail);
        Assert.NotNull(error);
    }
}
