# Corrección de diseño + detalle embebido — sesión 4

Branch: `feat/novedades-redesign`. Runner: `dotnet test Radar.slnx`. Baseline: build OK (tras matar Radar.Worker.exe PID 38552, con autorización), 264/264 tests.

Referencias visuales obligatorias: `C:\Users\DELL\Downloads\radar-novedades.html`, `C:\Users\DELL\Downloads\radar-detalle-version.html` (leídas como HTML/CSS). Ignorar: pestaña "Semana", "Guardados", "1 nueva" junto a Guías, novedad de tipo commits, "Guía de herramienta lista", fuentes de ejemplo, textos entre corchetes, el riel de iconos del detalle (el menú lateral completo se mantiene).

## Specs

- S1. Diagnóstico de resúmenes vacíos: `GEMINI_API_KEY` nunca configurada (confirmado: 0 filas en `ContentSummaries`/`DailyDigests`, 0 en todo scope de env, sin `.env`). No es bug de generación. Usuario pasó una clave por chat (2026-10-29); se guardó como variable de entorno de usuario, nunca escrita a archivo.
- S2. Fase A — layout Novedades: menú lateral ocupa toda la altura (sin barra superior de ancho completo); encabezado ("Novedades · N sin leer", "Revisado HH:mm", Actualizar ícono, tema) dentro de la columna de contenido; orden de menú Novedades→Guías/Mi stack→Aprender Gentle AI→Repositorios/Canales→Agregar fuente; columna de contenido centrada ~760px margen 48px; acciones en una fila debajo de cada novedad (no columna derecha); título con `TextWrapping=Wrap` máx. 2 líneas; pestaña de periodo activa en `TextBrush` (no `AccentBrush`); frase del día en `SecondaryTextBrush`; selector de tipo con chevron `Path`; `ScrollBar` delgada (6px) con estilo de tema; paginación con estilo de botón secundario.
- S3. Fase B — agregar `SuccessBrush` (verde) a Dark.xaml/Light.xaml para la etiqueta "Nuevo"; contraste ≥4.5:1 en ambos.
- S4. Fase C — ficha de detalle embebida, reemplaza `ReleaseDetailsWindow`:
  - Navegación: `ContentControl` en `MainWindow` alterna `NovedadesView`/`ReleaseDetailView` (UserControl), menú lateral siempre visible, "← Novedades" conserva fuente/periodo/tipo/página, recarga de 30s no saca del detalle, abrir detalle no marca como leído.
  - Prompt `Detalle/release-detail.prompt.md` + `.schema.json`: `resumen` (≤220c), `teAfecta{nivel: si|opcional|no, texto}`, `cambios[]{tipo: nuevo|cambio|arreglo|importante, texto}`, `instalacion[]{comando, opcional, deducido}`, `planDeUso[]`, `versiones[]{etiqueta, valor}`. Reglas: español, solo info de las notas, comandos deducidos del nombre del paquete marcan `deducido:true`, listas vacías si no hay datos.
  - `{{userOs}}`/`{{userShell}}` detectados en runtime (`RuntimeInformation`/`Environment`), overridables con `RADAR_USER_OS`/`RADAR_USER_SHELL`.
  - Tabla nueva `ContentDetails` (migración al final; no reusa `ContentSummaries` porque su PK es un registro por `ContentItemId` ya ocupado por el resumen corto).
  - Worker: genera fichas en segundo plano para no leídas más recientes, `MaxDetailsPerCycle` nuevo en `appsettings.json` sección `Summaries`.
  - Infrastructure: servicio que la vista llama una sola vez si falta la ficha (nada de lógica en code-behind).
  - Sin clave o si falla: título+metadatos+notas originales ya desplegadas (texto plano) + línea discreta con el motivo.
  - Video: misma vista, menos secciones (metadatos, título, `deQueTrata`, Ver en YouTube, descripción desplegable).
  - `ContentExplanations`/`ExplainReleaseAsync` reemplazados; tabla y datos NO se borran; si `ExplainReleaseAsync` deja de usarse, se elimina con sus tests y se documenta en el cierre.
- S5. No romper: recarga 30s (fuente/periodo/tipo/página), filtros+paginación, marcar como leído/abrir, tema, notificaciones de video, Worker y monitores, Agregar fuente, Aprender Gentle AI.
- S6. Tests nuevos: prompt+schema `release-detail`; parseo/validación de la ficha (secciones vacías no se muestran); migración nueva sin pérdida; límite `MaxDetailsPerCycle`; detección de SO con y sin `RADAR_USER_OS`; estado de navegación si se pone en Core.

## Tasks

| ID | Specs | Ruta | Estado |
|----|-------|------|--------|
| TA | S2 | inline | pending |
| TB | S3 | inline | pending |
| TC1 | S4 (prompt+Core+DB+tests) | inline | pending |
| TC2 | S4 (Infrastructure: generador+detección SO) | inline | pending |
| TC3 | S4 (Worker) | inline | pending |
| TC4 | S4 (Desktop: vistas+navegación) | inline | pending |
| TC5 | S4 (limpieza ReleaseDetailsWindow/ExplainReleaseAsync) | inline | pending |

## Log

- L1 (verbatim, resumido): "El resultado no se parece al diseño" + 2 imágenes de referencia obligatorias. Pide diagnóstico de resúmenes antes de tocar UI. Corrige layout de Novedades contra la imagen (menú a toda altura, encabezado dentro del contenido, orden de menú, columna centrada, acciones debajo, scrollbar/combo/paginación con estilo de tema). Rediseña el detalle: ficha embebida en MainWindow (no ventana aparte), generada sola por IA (prompt nuevo, tabla nueva, Worker en background + generación bajo demanda desde Infrastructure), con secciones condicionales y columna de versiones. Reemplaza ReleaseDetailsWindow/ExplainReleaseAsync sin borrar ContentExplanations. Sección 5 lista elementos de ejemplo a ignorar.
- L2 (2026-10-29): Diagnóstico confirmado contra la DB real (0 filas, GEMINI_API_KEY ausente en todo scope). Usuario pasó una clave por chat; se guardó como env var de usuario (nunca a archivo). Usuario aprobó el plan completo (fases A, B, C) con "vale".
