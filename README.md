# IA27 Terminal

> **ESTALINGRADO CORP · INTRA-NET** — agente de IA 100 % local, en consola, estilo Cyberpunk.

Terminal de inteligencia artificial que corre **modelos GGUF de forma local** con `llama.cpp`, sin Python, sin servicios cloud y sin enviar nada a un servidor externo. Hablas con el agente en español y, si lo autorizas, puede buscar en internet, **leer tus archivos, ejecutar comandos de PowerShell y crear archivos** — siempre bajo tu control.

![Captura de la IA27 Terminal](Capturas/ia27-terminal.png)

## Características

- **100 % local** — el modelo se ejecuta en tu equipo con `llama.cpp`. Nada sale de tu máquina salvo que tú lo autorices.
- **Sin Python** — C# / .NET 8 + binarios de `llama.cpp`. Ejecutable portable de un solo archivo.
- **Streaming real** — los tokens se imprimen mientras se generan. `Ctrl+C` detiene la respuesta en curso.
- **Búsqueda en internet con permiso** — el agente emite el marcador `[[NET]]`, el sistema te pregunta una sola vez por sesión y, si autorizas, busca por intención (lugares: Nominatim/OpenStreetMap; clima: Open-Meteo; resto: Wikipedia/DuckDuckGo con el texto de la página resultante) e inyecta los datos reales en el contexto.
- **Anti-alucinación** — si el modelo no está seguro de un dato (empresas, personas, eventos, precios, noticias), **busca en lugar de inventar**. Si la búsqueda no arroja nada, lo dice claramente.
- **Búsqueda explícita del usuario** — si escribes `busca en internet <tema>`, `investiga <tema>` o `<tema> en la web`, la terminal busca directamente sin depender del modelo.
- **Herramientas de agente (READ / CMD / WRITE)** — el agente puede leer archivos y carpetas, ejecutar comandos de PowerShell y crear o reescribir archivos mediante los marcadores `[[READ]]`, `[[CMD]]` y `[[WRITE]]`. Ejecución y escritura **siempre piden tu confirmación (s/n)**. Máximo 5 herramientas encadenadas por turno.
- **Modo harness (`/harness <objetivo>`)** — bucle agéntico autónomo: el agente planifica, usa herramientas paso a paso, verifica los resultados reales que le devuelve el sistema y termina por su cuenta emitiendo `[[DONE]]` con un resumen. Límite de 15 pasos y mismo régimen de permisos.
- **Gestor de modelos integrado** — descarga, lista y cambia entre modelos GGUF desde la propia terminal.
- **Diagnóstico** — comando `doctor` que comprueba modelo, runtime y hardware.

## Requisitos

- Windows x64
- .NET 8 SDK (solo para compilar; el ejecutable publicado es autocontenido)
- Un modelo `.gguf` (Qwen2.5-7B-Instruct Q4_K_M recomendado)
- Binarios de `llama.cpp` (`llama-server.exe`)

## Compilar

```bash
dotnet build -c Release
dotnet publish -c Release -o publish
```

El ejecutable queda en `publish/portable.exe` (self-contained, single-file, sin Python).

## Uso

```bash
portable.exe                    # abre la sesión interactiva (agente)
portable.exe listar             # lista los modelos GGUF instalados
portable.exe info 1             # datos del modelo 1
portable.exe preguntar "Hola"   # consulta única
portable.exe doctor             # diagnóstico del sistema
portable.exe descargar          # descarga modelos agente
portable.exe config show        # configuración actual
portable.exe help               # ayuda completa
```

### Mockup de la interfaz

```
╔═IR3C5.CORE═══════════════════════════════════════════════════════════════════╗
║                                     ####                                     ║
║                            ##       ####       ##                            ║
║                             ####    #####    ####                            ║
║                               #### ##### ####                                ║
║                                 ##########                                   ║
║                        ##        ##########        ##                        ║
║                          ####   ###########   ####                           ║
║                            ###################                               ║
║                       ################################                       ║
║                            ###################                               ║
║                          ####   ###########   ####                           ║
║                        ##        ##########        ##                        ║
║                                 ##########                                   ║
║                               #### ##### ####                                ║
║                             ####    #####    ####                            ║
║                            ##       ####       ##                            ║
║                                     ####                                     ║
╚══════════════════════════════════════════════════════════════════════════════╝
  INTRA-NET :: IA27 TERMINAL :: sistema local // canal seguro

                                Estado
╭──────────┬─────────────────────────────────────────────────────────╮
│ Campo    │ Valor                                                   │
├──────────┼─────────────────────────────────────────────────────────┤
│ Modelo   │ Atenea-Omega-IB2.gguf                                   │
│ Origen   │ …ve\Desktop\Estalingrado corp\proyectos\IA 27 T\modelos │
│ Motor    │ llama.cpp local                                         │
│ Red      │ ● ON (bajo autorización)                                │
│ Contexto │ 8192 tokens · 8 hilos · 0 capas GPU                     │
╰──────────┴─────────────────────────────────────────────────────────╯

Comandos rápidos
/help               ver comandos
/clear              limpiar historial
/use <modelo>       cambiar modelo
/net on|off         activar/desactivar red
/harness <objetivo> modo agéntico por pasos
/exit               salir
────────────────────────────────────────────────────────────────────────────────
Listo. Escribe /help para ver los comandos. Ctrl+C detiene la respuesta en curso.

Cargando el modelo; la primera carga puede tardar...

[INTRANET] ¿Permitir que el agente busque en internet automáticamente en esta sesión? (s/n): s
  Internet autorizado para esta sesión. El agente buscará automáticamente cuando necesite datos actuales.

tú> hola atenea
IA27> ¡Hola! ¿Cómo estás? Estoy aquí para ayudarte en lo que necesites. ¿En qué puedo asistirte hoy?
```

### Comandos del agente

| Comando | Función |
| --- | --- |
| `/help` | lista los comandos |
| `/clear` | limpia el historial |
| `/history` | muestra el historial de la sesión |
| `/system [texto]` | consulta o cambia la instrucción de sistema |
| `/modelos` | lista y cambia de modelo |
| `/descargar` | descarga un modelo nuevo |
| `/net [on\|off]` | activa o desactiva la búsqueda |
| `/harness <objetivo>` | modo agéntico por pasos (plan → herramientas → verificar → `[[DONE]]`) |
| `/temp`, `/rp`, `/topp` | ajusta muestreo |
| `/tokens [n]` | tokens máximos por respuesta |
| `/exit` | sale de la sesión |

### Búsqueda en internet

Al arrancar la sesión se pregunta **una sola vez** si autorizas que el agente busque automáticamente:

```
[INTRANET] ¿Permitir que el agente busque en internet automáticamente en esta sesión? (s/n):
```

- **Sí** — el agente busca por su cuenta cuando el dato es actual o no lo conoce, y te imprime `[buscando en la web...]` antes de responder.
- **No** — responde solo con su conocimiento local y te pide permiso cada vez que necesite buscar.

### Herramientas de agente

El agente dispone de tres herramientas sobre tu equipo. Puede decidir usarlas por su cuenta (igual que `[[NET]]`) o tú puedes pedirlo directamente con frases como `lee el archivo X`, `ejecuta <comando>` o `creame el archivo X que diga ...`:

| Marcador | Acción | Confirmación |
| --- | --- | --- |
| `[[READ]] ruta` | Lee un archivo (máx. 4000 chars) o lista una carpeta | No, salvo fuera del área de trabajo |
| `[[CMD]] comando` | Ejecuta PowerShell (cwd del proyecto, timeout 30 s) | Siempre (s/n) |
| `[[WRITE]] ruta :: contenido [[END]]` | Crea o reemplaza un archivo (con vista previa) | Siempre (s/n) |

Seguridad:

- Los comandos con patrones potencialmente destructivos (`Remove-Item -Recurse -Force`, `format`, `shutdown`, etc.) muestran una **advertencia en rojo** antes de pedir permiso.
- Las rutas se resuelven contra el área de trabajo; lecturas fuera de ella requieren permiso explícito.
- Los marcadores nunca se muestran en pantalla (buffer anti-fugas del streaming).
- Si deniegas una acción, el agente lo informa claramente en lugar de inventar el resultado.
- **Anti-alucinación de acciones** — si el modelo afirma "he creado el archivo X" o "ejecuté el comando Y" sin haber emitido el marcador, la terminal lo detecta, descarta la respuesta falsa y fuerza un reintento con el formato correcto (máx. 2). `[[WRITE]]` sin su cierre `[[END]]` se considera incompleto y **no crea nada**.
- El agente recuerda la última ruta usada por las herramientas en la sesión: pedidos como *"en esa carpeta"* o *"con el título mejoras"* se resuelven contra ella.

## Configuración

Se guarda en `config.json` (en la carpeta de la aplicación). Se puede ver y modificar desde la terminal:

```bash
portable.exe config show
portable.exe config set context 8192
portable.exe config set max-tokens 1024
portable.exe config set net on
portable.exe config path
```

Valores por defecto relevantes:

| Clave | Por defecto | Nota |
| --- | --- | --- |
| `context` | 4096 | ventana de contexto |
| `max-tokens` | 2048 | longitud máxima de respuesta |
| `threads` | 4 | hilos de CPU |
| `gpu-layers` | 0 | capas en GPU (subir si hay VRAM) |
| `temperature` | 0.7 | muestreo |
| `repeat-penalty` | 1.1 | evita repeticiones |
| `chat-template` | chatml | plantilla de chat |
| `cache-type-k` / `cache-type-v` | f16 | **no usar `q4_0`**: corrompe tokens en algunos GGUF |
| `net` | on | búsqueda web bajo autorización |

## Estructura del proyecto

```
IaTerminal.csproj           proyecto .NET 8
Program.cs                  punto de entrada y tema de consola
AppConfig.cs                configuración persistente
ModelCatalog.cs             escaneo de archivos .gguf
GgufMetadataReader.cs       lector de metadatos GGUF
LlamaServerSession.cs       sesión de inferencia (streaming SSE)
TerminalApplication.cs      lógica de terminal, comandos, búsqueda web y herramientas de agente
ModelRegistry.cs            catálogo de modelos descargables
Capturas/                   capturas de pantalla
```

## Ejecutar desde un pendrive (portable)

El ejecutable es **self-contained**: no necesita .NET instalado ni Python. Para usarlo desde un USB:

```
<stick>\IA 27 T\ia_terminal\publish\portable.exe   ← terminal
<stick>\IA 27 T\ia_terminal\publish\runtime\       ← llama-server.exe + DLLs
<stick>\IA 27 T\ia_terminal\publish\config.json    ← configuración (viaja con el USB)
<stick>\Modelo\Atenea-Omega-IB2.gguf                ← modelos (o en publish\models\)
```

- La **configuración se guarda junto al ejecutable**, así que el pendrive es autocontenido; si la carpeta no es escribible, cae a `%APPDATA%\IA27Terminal\config.json`.
- Los **modelos se localizan solos**: primero `publish\models\`, luego la ruta configurada, luego una carpeta `Modelo`/`models` hasta 3 niveles arriba del portable. Funciona con cualquier letra de unidad.
- Configurar con el propio ejecutable: `portable.exe config set model-dir "D:\Modelo"` y `portable.exe config set model Atenea-Omega-IB2.gguf`.
- **Importante**: si la salida del modelo es basura (caracteres sin sentido), el archivo `.gguf` está **corrupto** — una copia dañada con el mismo tamaño rompe la generación sin dar error. Verificalo con `Get-FileHash` contra el original o probando `runtime\llama-cli.exe -m <modelo> -p "hola" -n 32`.
- Antes de republicar sobre el pendrive, cerrá la terminal: el `.exe` en uso bloquea el `dotnet publish`.

## Problemas conocidos

- **`--cache-type q4_0` corrompe la salida** en determinados modelos GGUF (repeticiones como `Hola!ola!`, mezcla de tokens). Se usa `f16` por defecto de forma deliberada.
- Un modelo de 7B puede no obedecer el marcador `[[NET]]` a la primera; la terminal lo intercepta, amplía el patrón de negativas y permite forzar la búsqueda escribiéndola directamente en el prompt.

## Licencia

MIT
