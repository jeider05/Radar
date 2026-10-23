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

Cierra y vuelve a abrir la terminal o Visual Studio para que Radar la lea. Opcionalmente, `GEMINI_MODEL` cambia el modelo (por defecto `gemini-3.8-flash`).

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
