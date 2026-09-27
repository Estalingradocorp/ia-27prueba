# AGENTS.md — IA27 Terminal (Estalingrado Corp · Intra-net)

## Qué es

Terminal de agente IA 100 % local en C# / .NET 8 + `llama.cpp`, sin Python. Estética Cyberpunk (consola negra/cian/azul), español como idioma de interfaz, estilo de mensajes de sistema en español con corchetes (`[SISTEMA · ...]`, `[buscando en la web...]`).

## REGLA OBLIGATORIA — siempre actualizar el portable

> **Después de CUALQUIER cambio en el código fuente, hay que compilar Y publicar:**
>
> ```
> dotnet build -c Release
> dotnet publish -c Release -o publish
> ```
>
> El entregable real es `publish/portable.exe` (self-contained, single-file).
> Un cambio de código que no llega al portable **no existe para el usuario**.
> Nunca terminar una tarea de código sin dejar el portable regenerado y verificado.

`publish/` y `bin/`/`obj/` están en `.gitignore` (el portable pesa ~67 MB): el portable se actualiza **localmente**, no se sube al repo.

## Compilación y pruebas

- Build: `dotnet build -c Release` — debe terminar con 0 errores.
- Publish: `dotnet publish -c Release -o publish`.
- No hay suite de tests; verificación manual: probar los regex nuevos con PowerShell (mismo motor .NET) y correr el agente contra el modelo.

## Arquitectura

- `Program.cs` — punto de entrada, tema de consola.
- `AppConfig.cs` — configuración persistente (`config.json`).
- `LlamaServerSession.cs` — lanza `llama-server.exe`, streaming SSE a `/v1/chat/completions`, **políticas del system prompt** (`NetPolicy`, `ToolsPolicy`).
- `TerminalApplication.cs` — loop del agente, comandos `/`, búsqueda web (DuckDuckGo/Wikipedia), **herramientas de agente** (`[[READ]]`, `[[CMD]]`, `[[WRITE]]`), intercepción de negativas del modelo.

## Patrón de herramientas (extensión del agente)

El patrón probado es: marcador `[[XXX]]` emitido por el modelo (mensaje debe EMPEZAR con él, sin texto previo "copiable" en la política) → detección al final del stream → ejecución → resultado inyectado como mensaje `[SISTEMA · ...]` → `AskContinueAsync`. El **buffer anti-fugas** del streaming retiene desde el último `[`, así que ningún marcador llega a pantalla. Toda herramienta nueva debe seguir este patrón.

Reglas de seguridad vigentes:

- `[[CMD]]` y `[[WRITE]]` **siempre** piden confirmación (s/n), nunca autorización automática de sesión.
- Patrones destructivos en comandos → advertencia roja adicional.
- `[[READ]]` libre solo dentro del área de trabajo; fuera pide permiso.
- Máximo 5 herramientas encadenadas por turno, 2 rondas de `[[NET]]`.

## Problemas conocidos (no regresar)

- `--cache-type q4_0` corrompe tokens en algunos GGUF → usar `f16`/`f16`.
- Plantilla de chat forzada a `chatml` (el Jinja embebido de algunos GGUF falla).
- Si un modelo 7B "parotea" el texto de una política del system prompt, reescribir la política sin frases copiables (el buffer anti-fugas cubre el resto).
