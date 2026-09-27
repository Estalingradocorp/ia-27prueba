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

## Modo harness (`/harness <objetivo>`)

Bucle agéntico en `RunHarnessAsync` (TerminalApplication.cs): inyecta un mensaje `[SISTEMA · MODO HARNESS ACTIVADO]` con el objetivo → en cada paso el modelo hace UNA sola cosa: plan breve, UNA herramienta, o `[[DONE]]` + resumen. Límite: `HarnessMaxSteps` (15); límite alcanzado → cierre con aviso. Marcador malformado (p. ej. `[[WRITE]]` sin `[[END]]`) → no se ejecuta nada y se fuerza reintento del paso. Los permisos de `[[CMD]]`/`[[WRITE]]` se mantienen siempre.

Reglas de seguridad vigentes:

- `[[CMD]]` y `[[WRITE]]` **siempre** piden confirmación (s/n), nunca autorización automática de sesión.
- Patrones destructivos en comandos → advertencia roja adicional.
- `[[READ]]` libre solo dentro del área de trabajo; fuera pide permiso.
- Máximo 5 herramientas encadenadas por turno, 2 rondas de `[[NET]]`.

## Problemas conocidos (no regresar)

- `--cache-type q4_0` corrompe tokens en algunos GGUF → usar `f16`/`f16`.
- Plantilla de chat forzada a `chatml` (el Jinja embebido de algunos GGUF falla).
- Si un modelo 7B "parotea" el texto de una política del system prompt, reescribir la política sin frases copiables (el buffer anti-fugas cubre el resto).
- Un 7B **alucina acciones**: afirma "he creado el archivo X" sin emitir `[[WRITE]]`. Defensas en `TerminalApplication.cs`: `[[WRITE]]` exige cierre `[[END]]` (sin él no se crea nada), `WriteClaimPattern`/`CmdClaimPattern` interceptan afirmaciones sin herramienta y fuerzan reintento (máx. 2), y la intención explícita ("crea un txt/texto/tecto...") acepta ruta en cualquier posición, typos y referencias al contexto ("en esa carpeta", "con toda esta data") usando `lastToolPath`. No quitar estas trampas.
- Un 7B **alucina rutas**: inventa placeholders (`C:\Users\TuNombreDeUsuario\...`). Defensas: `BuildEnvironmentInfo` (LlamaServerSession.cs) inyecta usuario/cwd/Documentos/Escritorio REALES en el system prompt; `PlaceholderPathPattern` bloquea la ejecución y fuerza reemisión con la ruta real; alias "documentos/escritorio/descargas" resuelven a carpetas reales en la intención explícita; escrituras a la misma ruta en el mismo turno se bloquean (idempotencia).
- Al flushear colas del streaming hacia la consola, pasar SIEMPRE por `StripToolMarkers`: los fragmentos de marcador no deben llegar a pantalla.
- **Modelo corrupto en el pendrive**: una copia GGUF dañada (mismo tamaño, distinto hash) produce basura tipo `0C(G&&5B<#F06E...` sin dar ningún error. Diagnóstico: `Get-FileHash <modelo>` comparado con el original, o `runtime\llama-cli.exe -m <modelo> -p "hola" -n 32` (si sale basura, el modelo está corrupto). **Nunca confiar en que la copia "está ahí"**: verificar hash tras copiar al USB.

## Despliegue en pendrive (reglas)

Layout verificado y funcionando:
```
<stick>\IA 27 T\ia_terminal\publish\portable.exe   ← portable
<stick>\IA 27 T\ia_terminal\publish\runtime\       ← llama-server.exe y DLLs
<stick>\IA 27 T\ia_terminal\publish\config.json    ← config portable (junto al exe)
<stick>\Modelo\Atenea-Omega-IB2.gguf                ← modelos
```

- `AppConfig.GetDefaultConfigPath()` usa `AppContext.BaseDirectory\config.json` (viaja con el USB) y solo cae a `%APPDATA%` si la carpeta no es escribible.
- `ResolveModelDirectory` ordena: `models\` junto al exe (solo si contiene algún `.gguf`) → ruta configurada si existe → búsqueda de `Modelo/models/Modelos` hasta 3 niveles arriba del exe → ruta configurada. Así funciona con cualquier letra de unidad.
- `Save()` respeta la ruta de modelos que el usuario fija explícitamente (no la pisa la resolución automática).
- Publicar en el pendrive: `dotnet publish -c Release -o "D:\IA 27 T\ia_terminal\publish"`. **Requiere que `portable.exe` NO esté corriendo** (IOException al empaquetar).
- Si el portable se published mientras corre, primero cerrarlo.
- Al arrancar, un `llama-server.exe` huérfano de una sesión anterior puede impedir la carga (memoria y archivo de modelo tomados). Limpiar: `Get-Process llama-server | Stop-Process -Force`.
- Verificación rápida sin abrir la sesión: `portable.exe doctor` (todo `[OK]`) y `portable.exe listar` (debe mostrar el GGUF).
