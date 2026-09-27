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

## Icono del portable

- Fuente: `icono.ico` (multi-resolución 16/24/32/48/64/128/256 px, 32 bpp BGRA) en la raíz del proyecto.
- `IaTerminal.csproj` lo engancha con `<ApplicationIcon>icono.ico</ApplicationIcon>`: eso pone el icono en el `.exe` que muestra el Explorador.
- `Program.ApplyWindowIcon()` además pone el icono en la **ventana de consola** al arrancar, con `ExtractIconEx` + `WM_SETICON` (P/Invoke de `shell32`/`user32`). Lee el icono del propio ejecutable (`Environment.ProcessPath`), así que no depende de archivos sueltos ni de `System.Drawing.Common` (no disponible en .NET 8 sin paquete extra). Todo va envuelto en `try/catch`: si falla, la terminal arranca igual.
- Para regenerar el `.ico` desde un JPG hay que escribir las entradas **a mano** (DIB bottom-up + máscara AND). Ojo: la `BITMAPINFOHEADER` tiene 40 bytes y `biSize` va PRIMERO, en offset 0 — si se empieza escribiendo `biWidth` en offset 0 la cabecera queda corrida 4 bytes y `System.Drawing.Icon` / el Explorador rechazan el archivo sin error visible.
- Verificar que el icono realmente quedó: `ExtractAssociatedIcon` sobre `publish\portable.exe` y comparar píxeles contra el JPG original (la diferencia media por canal debe dar 0).

## Arquitectura

- `Program.cs` — punto de entrada, tema de consola.
- `AppConfig.cs` — configuración persistente (`config.json`).
- `LlamaServerSession.cs` — lanza `llama-server.exe`, streaming SSE a `/v1/chat/completions`, **políticas del system prompt** (`NetPolicy`, `ToolsPolicy`).
- `TerminalApplication.cs` — loop del agente, comandos `/`, búsqueda web por intención (dónde → Nominatim/OpenStreetMap; clima → Open-Meteo; general → Wikipedia con extracto + DuckDuckGo con el texto de la página ganadora — ver bitácora PARTE 22), **herramientas de agente** (`[[READ]]`, `[[CMD]]`, `[[WRITE]]`), intercepción de negativas del modelo.

## Patrón de herramientas (extensión del agente)

El patrón probado es: marcador `[[XXX]]` emitido por el modelo (mensaje debe EMPEZAR con él, sin texto previo "copiable" en la política) → detección al final del stream → ejecución → resultado inyectado como mensaje `[SISTEMA · ...]` → `AskContinueAsync`. El **buffer anti-fugas** del streaming retiene desde el último `[`, así que ningún marcador llega a pantalla. Toda herramienta nueva debe seguir este patrón.

- **Probar las trampas con el modelo real, no con "compiló".** La compuerta de artefacto en el chat, la reparación de rutas con espacios y el rechazo de carpetas se escribieron siguiendo la lógica, y los 4 bugs que aparecieron (ruta truncada en el espacio, `[[END]]` en un `[[READ]]`, denegación contada como escritura, bloque cercado dentro del archivo) **solo salieron al correr la prueba de verdad**. Usar una copia en `pruebas-tool\`, revisar que la carpeta quede sin archivos raros, y borrarla después. La prueba que se usó: el prompt "mejora el index que está aquí `<carpeta>` y hacé que se parezca a google" sobre un `index.html` de prueba, verificando **por hash** que el archivo cambió y que no quedó basura en el Escritorio.

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
- Un 7B tiene **DOS formas de no actuar** y hay que atrapar las dos: (a) **mentir** sobre la acción ("he creado el archivo") → `WriteClaimPattern`; (b) **negarse a actuar y enseñar el código** ("abrí el archivo y reemplazá el contenido" + el bloque completo en el chat) → la compuerta `LooksLikeArtifactInChat`. La (b) exige **las dos** condiciones juntas — bloque cercado con contenido de archivo entero (`FileArtifactPattern`) **y** indicación de hacerlo a mano (`ManualInstructionPattern`) — para no interceptar una explicación normal de código. Cuando dispara, **no imprime**: descarta y fuerza `[[WRITE]]` reutilizando el borrador del modelo (`ExtractDraftFromChat`), que si no costaría el doble de tokens.
- `ExplicitModifyIntent` cubre el pedido "mejora/actualizá/modificá/cambiá **un archivo que ya existe**", que `ExplicitWriteIntent` no ve (exige el verbo "crear"). Pide verbo de acción **más ruta real**, y excluye negaciones con lookbehind (`(?<!no\s)`, `(?<!sin\s)`, …) para no pedir una escritura prohibida. `DelegationDemandPattern` cubre el caso sin ruta: "tenés las herramientas hacelo vos".
- **Las rutas se truncan en los espacios** y esto NO es hypothetical: la carpeta del proyecto es "IA 27 T". Los patrones `[^\s"']+` cortan en el primer espacio, y el modelo escribe `[[WRITE]] C:\...\Desktop\IA` en vez de `C:\...\Desktop\IA 27 T\...`. Dos reparaciones, ambas usando **el disco como oráculo**: `ExtractRealPathFromText` (host: recorta desde el último espacio hasta que la ruta exista) y `RepairToolPathFromContext` (ruta del modelo: busca en el propio mensaje la ruta más larga que empiece por lo que él puso y exista). `ExecuteWriteTool` **rechaza escribir si la ruta es una CARPETA** — sin eso se crea un archivo basura con el nombre de la carpeta.
- Otras trampas de herramientas: un `[[END]]` pegado a un `[[READ]]`/`[[CMD]]` arruina la ruta (`TrimStrayEndMarker`); una escritura **denegada** no debe contar como ejecutada ni entrar en `writtenPaths` (`ToolWasDenied`), si no el reintento se come su propia guarda de duplicado; y si el modelo mete el bloque cercado dentro del archivo, el archivo arranca con ` ```html ` (`ExecuteWriteTool` lo recorta).
- **Para escribir un archivo largo en este modelo NO hay que bajar `max-tokens`**: con 400 el modelo agota los tokens antes de cerrar el `[[END]]` y el marcador queda incompleto. Con los 1536 de la configuración entra.
- Un 7B **alucina rutas**: inventa placeholders (`C:\Users\TuNombreDeUsuario\...`). Defensas: `BuildEnvironmentInfo` (LlamaServerSession.cs) inyecta usuario/cwd/Documentos/Escritorio REALES en el system prompt; `PlaceholderPathPattern` bloquea la ejecución y fuerza reemisión con la ruta real; alias "documentos/escritorio/descargas" resuelven a carpetas reales en la intención explícita; escrituras a la misma ruta en el mismo turno se bloquean (idempotencia).
- Al flushear colas del streaming hacia la consola, pasar SIEMPRE por `StripToolMarkers`: los fragmentos de marcador no deben llegar a pantalla.
- **Modelo corrupto**: una copia GGUF dañada (mismo tamaño, distinto hash) produce basura tipo `0C(G&&5B<#F06E...` sin dar ningún error. Diagnóstico: `Get-FileHash <modelo>` comparado con el original, o `runtime\llama-cli.exe -m <modelo> -p "hola" -n 32` (si sale basura, el modelo está corrupto). **Nunca confiar en que la copia "está ahí"**: verificar el hash. El hash sano de `Atenea-Omega-IB2.gguf` es `65B8FCD92AF6B4FEFA935C625D1AC27EA29DCB6EE14589C55A8F115CEAAA1423`.
- **Una sola copia del código y una sola del modelo.** Antes había dos carpetas de fuentes que divergían; ahora no. Si aparece una segunda copia, es un error.
- **Nunca hacer `git add -A` a ciegas.** Una vez la copia de trabajo mostró 3 archivos versionados como borrados (`D`) y commitear eso los habría perdido para siempre. Un `D` en `git status` significa "ausente del disco, presente en el repo": se recuperan con `git restore`, no se commitean.
- **El repositorio es el único respaldo.** Todo lo que importa va commiteado: el código, `icono.ico`, `AGENTS.md` y `IA27-BITACORA-MAESTRA-Y-REGLAS.txt`. Lo único que no entra es el `.gguf` (4,36 GB).
- **Búsqueda por intención (PARTE 22 de la bitácora).** El enrutado vive en `ClassifySearchIntent`/`SearchWebAsync`: `dónde` → Nominatim, `clima` → Open-Meteo, general → Wikipedia extracto + DDG con página ganadora. Reglas: (a) el `User-Agent` identificador de `CreateWebClient` es obligatorio para Nominatim, no quitarlo; (b) la geocodificación del clima lleva `countrycodes=ar` a propósito — sin eso "salta" matchea Salta Carnero (España), verificado en prueba real; la búsqueda general queda sin sesgo; (c) los verbos de intención se limpian en `CleanPlaceQuery`, no en la URL; (d) los recortes de ~1200-1600 caracteres por motor suben el contexto de 8192, no agrandarlos sin medir tokens.

## Layout y despliegue (reglas)

Layout verificado y funcionando. Todo en `C:\Users\nicot\OneDrive\Desktop\Estalingrado corp\proyectos\IA 27 T\` (antes `Desktop\IA 27 T\` — la carpeta se movió el 27/9/2026; revisar los DOS config.json tras cualquier mudanza de carpetas, no solo de discos — ver bitácora P15 puntos 27 y 34):
```
IA 27 T\
  ia_terminal\                      ← repo: código, .git, AGENTS.md, bitácora
  ia_terminal\publish\portable.exe  ← portable (el entregable real)
  ia_terminal\publish\runtime\      ← llama-server.exe y DLLs
  ia_terminal\publish\config.json   ← config del portable de desarrollo
  portable 0.2.exe                  ← entrega congelada
  runtime\                          ← runtime de la entrega
  config.json                       ← config de la entrega
  modelos\                          ← el .gguf
  logs\                             ← lo crea solo al arrancar
```

- `AppConfig.GetDefaultConfigPath()` usa `AppContext.BaseDirectory\config.json` (junto al .exe) y solo cae a `%APPDATA%` si la carpeta no es escribible.
- `ResolveModelDirectory` ordena: `models\` junto al exe (solo si contiene algún `.gguf`) → ruta configurada si existe → búsqueda de `Modelo/models/Modelos` hasta 3 niveles arriba del exe → ruta configurada.
- `Save()` respeta la ruta de modelos que el usuario fija explícitamente (no la pisa la resolución automática).
- **Hay DOS `config.json`**, uno por ejecutable, y cambiár uno no cambia el otro. Para cambiar la ruta del modelo hay que usar `portable.exe config set model-dir "<ruta>"` en los DOS, no editar el archivo a mano.
- Publicar: `dotnet publish -c Release -o publish`. **Requiere que `portable.exe` NO esté corriendo** (IOException al empaquetar).
- Al arrancar, un `llama-server.exe` huérfano de una sesión anterior puede impedir la carga (memoria y archivo de modelo tomados). Limpiar: `Get-Process llama-server | Stop-Process -Force`.
- El entregable congelado NO es un .exe suelto: al lado tiene que estar `runtime\` y su propio `config.json`, si no arranca resolviendo a una ruta de C: y `doctor` da OK mentiroso.
- Verificación rápida sin abrir la sesión: `portable.exe doctor` (todo `[OK]`) y `portable.exe listar` (debe mostrar el GGUF). Un `doctor` OK no alcanza: hay que mirar de dónde sacó el runtime y el modelo.

## Documentación

- `IA27-BITACORA-MAESTRA-Y-REGLAS.txt` (en la raíz del repo) es la bitácora maestra: métodos, reglas, trampas encontradas e historial de incidentes. **Todo comportamiento nuevo o trampa encontrada se anota ahí y acá.** Los números concretos (hash del modelo, hash del .exe congelado, tokens/s, configuración óptima) viven en la bitácora, no repetidos acá.
