# Novedades: lista con resúmenes automáticos — sesión 3 de 3

Branch: `feat/novedades-redesign`. Runner: `dotnet test Radar.slnx`. Baseline: build OK (tras matar un Radar.Worker.exe residual con autorización), 249/249 tests.

## Specs

- S1. La interfaz **nunca llama a Gemini**; solo lee la base de datos.
- S2. No romper: recarga 30s sin solapar conservando fuente; filtro por fuente del menú lateral + contadores; periodos `All/Today/ThisMonth/ThisYear` y paginación de 10; Marcar como leído/Detalles/Abrir (solo http/https); estado vacío y errores en `StatusText`; Refresh y tema en encabezado; notificaciones de video; "Generar explicación con IA" en `ReleaseDetailsWindow` sin cambios; menú lateral de sesión 2.
- S3. Nada de lógica de negocio nueva en code-behind: filtros y preparación de datos van en `Radar.Core` como funciones puras, con tests. Sin refactor grande, sin MVVM. Colores solo vía `{DynamicResource}`, nada hardcodeado. URL de miniatura de YouTube: constante en `Radar.Infrastructure.YouTube`.
- S4. Diseño: líneas finas (`DividerBrush`) en vez de tarjetas; colores de paleta (`TextBrush` títulos, `SecondaryTextBrush` fuente, `BodyTextBrush` resúmenes, `MutedTextBrush` metadatos/acciones, `AccentBrush` puntos no leído, `AiBrush` ícono IA). Tamaños: título pantalla 24 SemiBold, título novedad 16-17 SemiBold, texto 14, metadatos 13. Columna de contenido ~760px centrada, margen 32-48px. 22px de espacio por novedad. Clicables ≥36px, foco visible, `AutomationProperties.Name` en botones solo-ícono.
- S5. Encabezado: izquierda título ("Novedades" o fuente) + "N sin leer" en `MutedTextBrush`; derecha `StatusText`, botón ícono Actualizar, botón de tema.
- S6. Resumen del día: una sola línea, ícono ✦ en `AiBrush` + frase de `DailyDigests` de hoy (fecha local). Si no existe, nada (ni espacio ni "cargando"). Solo en vista general "Novedades".
- S7. Filtros en una fila con línea inferior: periodos como pestañas de texto (Todo/Hoy/Este mes/Este año, mapeados exactamente a `ContentPeriod`); selector de tipo (Todos los tipos/Releases/Videos) que filtra por `SourceType` antes de paginar y vuelve a página 1 al cambiar — función pura en Core con tests.
- S8. Lista sin tarjetas, separador `DividerBrush` entre novedades. Release: metadatos 13px (punto 6px ámbar si no leído, fuente, "· Release", fecha a la derecha), título clicable (abre Detalles, mismo `Details_Click`), resumen como viñetas (máx 3 `puntos` + `accion` si existe como última), acciones discretas en texto (`MutedTextBrush`→hover `TextBrush`, alcanzables por teclado). Video: miniatura 176×99 esquinas 8px (URL vía constante; fallback `HoverBrush` + ícono play si no carga), metadatos "Canal · Video · fecha", título, frase `deQueTrata`, mismas acciones. Leído: igual que hoy (sin punto, título normal/atenuado, sin "Marcar como leído").
- S9. Estado del resumen por novedad: Listo → viñetas/frase; Pendiente → "Resumen en camino…" en `MutedTextBrush`; sin `GEMINI_API_KEY` → nada; Error → nada. Decisión registrada (L2): Desktop infiere "configurado" leyendo su propia env var `GEMINI_API_KEY` una vez al iniciar, ya que la DB no distingue "sin clave" de "pendiente".
- S10. Estado vacío y paginación: misma lógica, estilo nuevo, textos en español.
- S11. Lectura de resúmenes y digest en la misma recarga de 30s, mismo `Task.Run`, con `ContentSummaryStore`. La detección de cambios incluye los resúmenes (y el digest). Al llegar un resumen nuevo, la lista se redibuja conservando fuente/periodo/tipo/página. `NewsItem` (Core) o extensión de `ContentItemRow` con los datos del resumen ya listos; sin parseo en XAML/code-behind.
- S12. Tests nuevos: filtro por tipo; preparación de filas con resumen (listo/pendiente/sin resumen); detección de cambios con resúmenes (en Core).
- S13. Revisión final manual de las 3 sesiones (checklist del pedido original, sección 5).

## Tasks

| ID | Specs | Ruta | Estado |
|----|-------|------|--------|
| T1 | S3, S7, S9, S11, S12 (Core) | inline | pending |
| T2 | S3 (constante YouTube) | inline | pending |
| T3 | S5-S10 (Desktop XAML+code-behind) | inline | pending |
| T4 | S13 (verificación final) | inline | pending |

## Log

- L1 (verbatim, resumido): Sesión 3 de 3. Rediseñar la lista de Novedades y mostrar los resúmenes automáticos leyendo solo la DB (nunca Gemini). No romper recarga/filtros/acciones/notificaciones/explicación IA/menú lateral. Filtros y preparación de datos en Core puro con tests. Diseño: líneas finas, paleta de sesión 2, tamaños específicos, columna centrada ~760px. Encabezado con título+no leídos+Actualizar(ícono)+tema. Línea de resumen del día (AiBrush, solo en vista general). Filtros en una fila (periodos + tipo). Lista sin tarjetas: release con viñetas, video con miniatura 176×99 (constante de URL) y frase. Estados de resumen: listo/pendiente/sin clave/error. Revisión final de las 3 sesiones con checklist explícito.
- L2 (2026-10-29): Usuario aprobó el plan, incluida la heurística de "Pendiente" (Desktop lee su propia `GEMINI_API_KEY`) y el cambio de Refresh a botón-ícono sin color de acento.
