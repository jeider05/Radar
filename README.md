# Radar

Aplicación de escritorio para Windows que sigue las novedades de los proyectos que te interesan: releases de repositorios públicos de GitHub y videos nuevos de canales de YouTube. Las guarda en una base de datos local, las marca como pendientes y puede explicar los releases en español con Gemini.

## Puesta en marcha rápida

1. Instala los requisitos (ver tabla siguiente).
2. Clona y compila:
   ```powershell
   git clone https://github.com/jeider05/Radar.git
   cd Radar
   dotnet build Radar.slnx
   ```
3. Ejecuta Radar:
   - **Visual Studio:** abre `Radar.slnx`, establece `Radar.Desktop` como proyecto de inicio y pulsa F5.
   - **Terminal:** `dotnet run --project src/Radar.Desktop`
4. Añade tus fuentes en **Manage sources** (repositorios de GitHub o canales de YouTube).

Al abrirse, Radar crea la base de datos y arranca el proceso de monitorización (`Radar.Worker`).

## Requisitos

| Requisito | Detalle |
|-----------|---------|
| Sistema operativo | Windows 10 versión 2004 o posterior, o Windows 11 |
| .NET | .NET 10 SDK, o Visual Studio 2026 con «Desarrollo de escritorio de .NET» |
| Git | Para clonar el repositorio |
| Clave de Gemini | Opcional. Solo para las explicaciones con IA |

## Configurar Gemini (opcional)

La clave se lee de la variable de entorno `GEMINI_API_KEY`. Nunca se guarda en el código, en Git ni en la base de datos.

```powershell
[Environment]::SetEnvironmentVariable("GEMINI_API_KEY", "tu-clave", "User")
```

Cierra y vuelve a abrir la terminal o Visual Studio para que Radar la lea. `.env.example` en la raíz documenta estas variables (sin valores reales); copialas a tu entorno real, nunca a un `.env` que se suba a Git.

| Variable | Qué hace |
|----------|----------|
| `GEMINI_API_KEY` | Clave de la API de Gemini. Sin ella, Radar sigue funcionando pero no genera explicaciones ni resúmenes con IA |
| `GEMINI_MODEL` | Modelo de Gemini a usar. Opcional, por defecto `gemini-3.8-flash` |
| `RADAR_PROMPTS_DIR` | Carpeta donde buscar los prompts (`.prompt.md`/`.schema.json`). Opcional, por defecto la carpeta `Prompts` junto al ejecutable |

### Prompts como archivos

Los prompts de IA no están escritos en el código: viven en `src/Radar.Infrastructure/Prompts/` como archivos `.prompt.md` (encabezado YAML + secciones `## System`/`## User` con variables `{{nombre}}`) y, cuando la respuesta es JSON, un `.schema.json` junto a cada uno. `PromptLibrary` los carga, separa y reemplaza variables en tiempo de ejecución.

### Resúmenes automáticos (Worker)

El Worker resume en 1-3 frases cada release o video nuevo con Gemini y guarda el resultado en la tabla `ContentSummaries`, además de un digest diario ("Tu radar hoy") en `DailyDigests`. Se configura en `src/Radar.Worker/appsettings.json`, sección `Summaries`:

| Opción | Qué hace |
|--------|----------|
| `Enabled` | Activa o desactiva el monitor de resúmenes |
| `PollingInterval` | Tiempo entre ciclos |
| `MaxItemsPerCycle` | Máximo de novedades resumidas por ciclo, para cuidar la cuota de Gemini |
| `MaxAgeDays` | No resume novedades más antiguas que esto |
| `OnlyUnread` | Si es `true`, solo resume novedades no leídas |
| `DailyDigestMinInterval` | Mínimo tiempo entre dos digests diarios de la misma fecha |

Sin `GEMINI_API_KEY`, este monitor no hace nada (deja un aviso una sola vez en el log) y el resto del Worker sigue funcionando igual.

## Datos locales

| Qué | Dónde |
|-----|-------|
| Base de datos SQLite | `%LOCALAPPDATA%\Radar\radar.db` |
| Tema claro u oscuro elegido | `%LOCALAPPDATA%\Radar\desktop-theme.txt` |

- La base de datos se crea automáticamente al primer arranque y aplica sus migraciones sola.
- Los datos no forman parte del repositorio: en un computador nuevo Radar empieza vacío.

### Llevar tus datos a otro computador

1. En el computador de origen, cierra Radar y detén `Radar.Worker` desde el Administrador de tareas.
2. Copia `%LOCALAPPDATA%\Radar\radar.db`.
3. Pégalo en la misma ruta del computador de destino **antes** de abrir Radar allí.

Así conservas fuentes, estados de lectura y explicaciones generadas.

## Aprender Gentle AI

El botón **Aprender Gentle AI** abre un módulo de estudio con tres vistas:

| Vista | Qué hace |
|-------|----------|
| Aprender | Temas agrupados por categoría, con su estado (pendiente, en estudio, estudiado). Cada tema separa la explicación didáctica de lo confirmado en la documentación oficial y muestra la fuente y la fecha de consulta |
| Exámenes | Examen general o por tema, de 5, 10 o 20 preguntas, con dificultad básica, variada o avanzada. La solución solo se muestra después de responder. Permite repetir las preguntas falladas |
| Mi progreso | Historial de exámenes, temas pendientes y conceptos con más errores |

- El catálogo de temas y preguntas viene integrado en la aplicación y se carga en la base de datos al abrir el módulo, sin tocar tu progreso.
- Con Gemini configurado, **Generar preguntas con IA** crea preguntas nuevas de un tema usando solo sus hechos confirmados. Las preguntas repetidas se descartan y cada una indica su origen.

## Cómo funciona

| Parte | Responsabilidad |
|-------|-----------------|
| `Radar.Desktop` (WPF) | Interfaz. Inicia y supervisa el Worker, muestra novedades y notificaciones de Windows |
| `Radar.Worker` | Proceso independiente que consulta GitHub (cada 60 min) y los feeds de YouTube (cada 15 min). Sigue funcionando al cerrar la ventana |
| `Radar.Core` | Modelos y reglas (periodos, paginación, notificaciones) |
| `Radar.Infrastructure` | SQLite, clientes de GitHub, YouTube y Gemini |

Los intervalos se configuran en `src/Radar.Worker/appsettings.json`.

## Tests

```powershell
dotnet test Radar.slnx
```

## Notas

- Antes de recompilar, cierra `Radar.Worker` si sigue en ejecución; bloquea sus archivos de salida.
- Las notificaciones de videos nuevos solo aparecen mientras Radar está abierto (también minimizado). Si está cerrado, los videos quedan como pendientes.
