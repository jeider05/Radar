# Novedades redesign — sesión 1 de 3

Branch: `feat/novedades-redesign`. Runner: `dotnet test Radar.slnx`. Baseline: build OK, 196/196 tests pass (after killing a stale Radar.Worker.exe PID 6216 that locked build output).

## Specs

- S1. Mover los prompts de IA a archivos, uno por prompt, bajo `src/Radar.Infrastructure/Prompts/`:
  - `Novedades/release-summary.prompt.md` + `.schema.json`
  - `Novedades/video-summary.prompt.md` + `.schema.json`
  - `Novedades/daily-digest.prompt.md` + `.schema.json`
  - `Detalle/release-explanation.prompt.md` (prompt actual de `GeminiClient.SystemInstruction`, movido sin cambios de texto)
- S2. Formato `.prompt.md`: encabezado YAML (`name`, `version` entero, `description`), sección `## System`, sección `## User` con variables `{{nombre}}` (ej. `{{projectName}}`, `{{title}}`, `{{publishedAt}}`, `{{url}}`, `{{notes}}`).
- S3. `PromptLibrary` / `IPromptProvider` en Infrastructure: lee el archivo por nombre, separa System y User, reemplaza variables. Si falta una variable o el archivo, lanza un error claro. Archivos con `CopyToOutputDirectory=PreserveNewest`, leídos desde `AppContext.BaseDirectory/Prompts` o `RADAR_PROMPTS_DIR`.
- S4. `ExplainReleaseAsync` carga el prompt desde `Detalle/release-explanation.prompt.md`; `GeminiClientTests` sigue pasando (ajustado solo para esto).
- S5. `ContentSummaryMonitor` (`BackgroundService`) + `SummaryOptions` en sección `"Summaries"` de `appsettings.json`:
  ```json
  "Summaries": {
    "Enabled": true,
    "PollingInterval": "00:02:00",
    "MaxItemsPerCycle": 5,
    "MaxAgeDays": 30,
    "OnlyUnread": true,
    "DailyDigestMinInterval": "00:30:00"
  }
  ```
  - Cada ciclo: busca novedades sin resumen o con versión de prompt anterior; respeta `OnlyUnread`/`MaxAgeDays`, empezando por las más recientes; genera como máximo `MaxItemsPerCycle`, una por una.
  - Sin `GEMINI_API_KEY`: aviso una sola vez, no hace nada más.
  - Gemini 429: espera al siguiente ciclo, sin reintentos agresivos.
  - Registrado en `Program.cs` como los otros monitores, con su propio `HttpClient` (`GeminiClient.CreateHttpClient()`).
- S6. `GeminiClient.GenerateJsonAsync(...)`: `generationConfig.responseMimeType = "application/json"` + `responseSchema` del `.schema.json`. No rompe `ExplainReleaseAsync`.
  - Esquema release: `puntos` (1-3 frases, ≤140 caracteres c/u), `accion` (string o null).
  - Esquema video: `deQueTrata` (≤180 caracteres).
  - Esquema digest diario: `frase` (≤200 caracteres).
  - Reglas comunes: responder en español; usar solo la información recibida; no inventar versiones/comandos/funcionalidades; si no hay notas/descripción, decirlo.
  - Validar la respuesta en C# (campos obligatorios y longitudes); si no es válida, se guarda como error.
  - Videos: resumidos con título + `ContentItem.Description`, sin transcripciones.
- S7. Migración nueva al final de `RadarDatabase` (no toca `ContentExplanations`):
  - `ContentSummaries`: `ContentItemId` (PK/FK ContentItems), `Kind` (`release`|`video`), `Json`, `PromptVersion`, `Model`, `Status` (`ready`|`error`), `Error`, `CreatedAtUtc`.
  - `DailyDigests`: `Date` (`yyyy-MM-dd`, PK), `Json`, `ItemsHash`, `Model`, `CreatedAtUtc`.
- S8. `ContentSummaryStore` (Infrastructure) + modelos Core: `ContentSummary`, `ReleaseSummary`, `VideoSummary`, `DailyDigest`, con parseo de JSON. Métodos de lectura para la sesión 3: todos los resúmenes listos; resumen diario de una fecha.
- S9. Digest diario se regenera solo cuando cambia `ItemsHash` del conjunto de novedades de hoy, y como máximo cada `DailyDigestMinInterval`; se arma con resúmenes ya generados del día, no con notas completas. Errores vía `ILogger` en el Worker.
- S10. No romper: Worker (arranque/supervisión/mutex), `GitHubImportCommand`, migraciones existentes, ruta `%LOCALAPPDATA%\Radar\radar.db`, intervalos GitHub 60min/YouTube 15min, explicación de `ReleaseDetailsWindow` idéntica, UI (`MainWindow`, `SourcesWindow`, notificaciones) sin tocar en esta sesión.
- S11. Sin credenciales quemadas (Gemini sigue usando `GEMINI_API_KEY`/`GEMINI_MODEL`), sin rutas absolutas quemadas, config no secreta en `appsettings.json` con `IOptions<T>`.
- S12. `.env.example` en la raíz con todas las variables de entorno, sin valores reales, un comentario por variable. Documentar lo nuevo en `README.md`.
- S13. Tests nuevos en `tests/Radar.Infrastructure.Tests` (estilo `FakeHttpMessageHandler`): `PromptLibrary` (carga, separación, variables, error si falta); prompt movido idéntico al anterior; `GenerateJsonAsync` (responseMimeType/responseSchema, errores 400/401/429); migración nueva (no pierde datos); `ContentSummaryStore` y parseo/validación de cada tipo; `ContentSummaryMonitor` (sin clave no hace nada, respeta `MaxItemsPerCycle`, salta lo ya resumido con misma versión de prompt).

## Tasks

| ID | Specs | Ruta | Estado |
|----|-------|------|--------|
| T1 | S1-S4 | inline | done (c02be52) |
| T2 | S6 | inline | done (c02be52) |
| T3 | S7-S8 | inline | pending — about to commit |
| T4 | S5, S9, S13 (monitor tests) | inline | pending — about to commit |
| T5 | S11-S12 | inline | pending |
| T6 | S13 (remaining) | inline | done incrementally with T1-T4 |

## Log

- L1 (verbatim, resumido del pedido original): Sesión 1 de 3, no tocar interfaz. Mover prompts de IA a archivos y hacer que el Worker genere un resumen corto por novedad guardado en la base de datos. Reglas de no-romper-nada (worker, DB, monitores, explicación IA, interfaz), arquitectura Core/Infrastructure/Worker, sin credenciales/rutas quemadas, `.env.example`, prompts como archivos (formato YAML+System+User), `PromptLibrary`/`IPromptProvider`, `ContentSummaryMonitor` + `SummaryOptions`, `GenerateJsonAsync` con schema, tablas `ContentSummaries`/`DailyDigests`, `ContentSummaryStore`, digest diario con `ItemsHash` y `DailyDigestMinInterval`, tests nuevos, commits pequeños Conventional Commits, resumen final.
- L2 (2026-10-27): Usuario aprobó el plan ("vale si"). Decisiones de diseño registradas: (a) prompts compartidos como `<None Link>` en Desktop/Worker/Tests porque el contenido de una librería no se propaga automáticamente a los ejecutables que la referencian; (b) `ExplainReleaseAsync` mantiene su firma actual, carga el prompt internamente (lazy) para no tocar `ReleaseDetailsWindow.xaml.cs`; (c) `GenerationConfig` gana `ResponseSchema` opcional (default null) para no romper `GenerateStudyQuestionsAsync`.
