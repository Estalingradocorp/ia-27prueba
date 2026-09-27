using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IaTerminal;

public sealed class TerminalException : Exception
{
    public TerminalException(string message) : base(message)
    {
    }

    public TerminalException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public sealed class TerminalApplication
{
    private AppConfig config = new(null);
    private ModelCatalog catalog = new(AppConfig.DefaultModelDirectory);
    private bool jsonOutput;
    private bool noBanner;
    

    public async Task<int> RunAsync(string[] args)
    {
        config = AppConfig.Load();
        var positional = ApplyOptions(args);
        config.Normalize();
        catalog = new ModelCatalog(config.ModelDirectory);

        if (positional.Count == 0)
        {
            return await RunAgentAsync(null);
        }

        var command = positional[0].ToLowerInvariant();
        var rest = positional.Skip(1).ToArray();
        return command switch
        {
            "help" or "ayuda" or "--help" or "-h" => RunHelp(),
            "version" or "--version" => RunVersion(),
            "list" or "listar" or "models" or "modelos" => RunList(rest),
            "info" or "informacion" => RunInfo(rest.FirstOrDefault()),
            "run" or "ask" or "preguntar" => await RunOneShotAsync(rest),
            "agent" or "agente" or "chat" => await RunAgentAsync(rest.FirstOrDefault()),
            "doctor" or "diagnostico" => await RunDoctorAsync(),
            "config" or "configuracion" => RunConfig(rest),
            "serve" or "servidor" => await RunServerAsync(rest.FirstOrDefault()),
            "descargar" or "download" => await RunDownloadMenu(rest),
            _ => throw new TerminalException($"Comando desconocido: {command}. Usa 'help'.")
        };
    }

    private List<string> ApplyOptions(string[] args)
    {
        var positional = new List<string>();
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--model-dir":
                    config.ModelDirectory = ReadOptionValue(args, ref index, argument);
                    break;
                case "--runtime-dir":
                    config.RuntimeDirectory = ReadOptionValue(args, ref index, argument);
                    break;
                case "--model":
                    config.SelectedModel = ReadOptionValue(args, ref index, argument);
                    break;
                case "--context":
                    config.ContextSize = ReadIntOption(args, ref index, argument);
                    break;
                case "--max-tokens":
                    config.MaxTokens = ReadIntOption(args, ref index, argument);
                    break;
                case "--threads":
                    config.Threads = ReadIntOption(args, ref index, argument);
                    break;
                case "--gpu-layers":
                    config.GpuLayers = ReadIntOption(args, ref index, argument);
                    break;
                case "--temperature":
                    config.Temperature = ReadDoubleOption(args, ref index, argument);
                    break;
                case "--repeat-penalty":
                    config.RepeatPenalty = ReadDoubleOption(args, ref index, argument);
                    break;
                case "--top-p":
                    config.TopP = ReadDoubleOption(args, ref index, argument);
                    break;
                case "--chat-template":
                    config.ChatTemplate = ReadOptionValue(args, ref index, argument);
                    break;
                case "--timeout":
                    config.StartupTimeoutSeconds = ReadIntOption(args, ref index, argument);
                    break;
                case "--json":
                    jsonOutput = true;
                    break;
                case "--no-banner":
                    noBanner = true;
                    break;
                default:
                    positional.Add(argument);
                    break;
            }
        }

        return positional;
    }

    private int RunHelp()
    {
        Console.ForegroundColor = ConsoleColor.DarkBlue;
        Console.WriteLine("//== ESTALINGRADO CORP · INTRA-NET ==//");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("IA27 Terminal portable · agente IA local (modelos GGUF, sin Python)");
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine();
        Console.WriteLine("Uso:");
        Console.WriteLine("  portable.exe [comando] [argumentos]");
        Console.WriteLine();
        Console.WriteLine("Comandos:");
        Console.WriteLine("  listar                         lista los modelos GGUF");
        Console.WriteLine("  info [modelo]                  muestra los datos del modelo");
        Console.WriteLine("  agente [modelo]                abre una sesión interactiva");
        Console.WriteLine("  preguntar [modelo] <mensaje>   ejecuta una consulta única");
        Console.WriteLine("  servidor [modelo]              inicia el servidor local API");
        Console.WriteLine("  doctor                         comprueba modelo, runtime y hardware");
        Console.WriteLine("  descargar                       descarga o selecciona modelos agente");
        Console.WriteLine("  config show|set|reset|path     gestiona la configuración");
        Console.WriteLine("  help                           muestra esta ayuda");
        Console.WriteLine();
        Console.WriteLine("Ejemplos:");
        Console.WriteLine("  portable.exe listar");
        Console.WriteLine("  portable.exe preguntar \"Explica qué es un agente local\"");
        Console.WriteLine("  portable.exe agente");
        Console.WriteLine();
        Console.WriteLine("Opciones globales:");
        Console.WriteLine("  --model-dir RUTA               cambia la carpeta de modelos");
        Console.WriteLine("  --runtime-dir RUTA             cambia la carpeta de llama_bin");
        Console.WriteLine("  --model SELECTOR               selecciona el modelo por nombre o número");
        Console.WriteLine("  --context N --max-tokens N      ajusta contexto y respuesta");
        Console.WriteLine("  --threads N --gpu-layers N      ajusta CPU y GPU");
        Console.WriteLine("  --temperature N                ajusta la temperatura");
        Console.WriteLine("  --repeat-penalty N             evita repeticiones (1.1 por defecto)");
        Console.WriteLine("  --top-p N                      muestreo top-p (0.9 por defecto)");
        Console.WriteLine("  --chat-template NOMBRE         plantilla de chat (chatml por defecto)");
        Console.WriteLine("  --timeout N                    segundos de espera al cargar el modelo");
        Console.WriteLine("  --json                         salida JSON para listar");
        return 0;
    }

    private int RunVersion()
    {
        var version = typeof(TerminalApplication).Assembly.GetName().Version?.ToString() ?? "1.0.0";
        Console.WriteLine($"IA27 Terminal {version}");
        Console.WriteLine("Runtime: C#/.NET 8 + llama.cpp; Python requerido: no");
        return 0;
    }

    private int RunList(IReadOnlyList<string> rest)
    {
        var selectedCatalog = rest.Count > 0 ? new ModelCatalog(rest[0]) : catalog;
        var models = selectedCatalog.List();
        if (jsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(models, new JsonSerializerOptions { WriteIndented = true }));
            return models.Count == 0 ? 1 : 0;
        }

        Console.WriteLine($"Carpeta: {selectedCatalog.Root}");
        if (models.Count == 0)
        {
            Console.WriteLine("No se encontraron archivos .gguf.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("ID   MODELO                                      TAMAÑO       MODIFICADO");
        Console.WriteLine("---  ------------------------------------------  -----------  --------------------");
        for (var index = 0; index < models.Count; index++)
        {
            var model = models[index];
            Console.WriteLine($"{index + 1,-3}  {Truncate(model.RelativePath, 42),-42}  {FormatBytes(model.SizeBytes),-11}  {model.LastWriteTimeLocal:yyyy-MM-dd HH:mm}");
        }

        Console.WriteLine();
        Console.WriteLine("Usa 'info 1', 'agente 1' o 'preguntar 1 mensaje'.");
        return 0;
    }

    private int RunInfo(string? selector)
    {
        var model = ResolveModel(selector);
        var gguf = GgufMetadataReader.TryRead(model.Path);
        var runtime = RuntimeLocator.Find(config.RuntimeDirectory, config.ModelDirectory);
        var architecture = gguf is null ? string.Empty : MetadataText(gguf.Metadata, "general.architecture");
        var context = gguf is null ? null : MetadataValue(gguf.Metadata, architecture, "context_length");
        var parameterCount = gguf is null ? null : GeneralParameterCount(gguf.Metadata);
        var quantization = gguf is null ? string.Empty : QuantizationName(GeneralMetadataValue(gguf.Metadata, "general.file_type"), model.Name);

        Console.WriteLine($"Nombre:       {model.Name}");
        Console.WriteLine($"Ruta:         {model.Path}");
        Console.WriteLine($"Tamaño:       {FormatBytes(model.SizeBytes)} ({model.SizeBytes:N0} bytes)");
        Console.WriteLine($"Modificado:   {model.LastWriteTimeLocal:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"Formato:      GGUF v{(gguf?.Version.ToString() ?? "desconocido")}");
        Console.WriteLine($"Arquitectura: {(string.IsNullOrWhiteSpace(architecture) ? "desconocida" : architecture)}");
        Console.WriteLine($"Cuantización: {(string.IsNullOrWhiteSpace(quantization) ? "desconocida" : quantization)}");
        Console.WriteLine($"Parámetros:   {FormatParameterCount(parameterCount)}");
        Console.WriteLine($"Contexto:     {FormatValue(context)}");
        Console.WriteLine($"Tensores:     {gguf?.TensorCount.ToString() ?? "desconocido"}");
        Console.WriteLine($"Runtime:      {runtime ?? "no encontrado"}");
        if (gguf is null)
        {
            Console.WriteLine("Cabecera:     no se pudo leer; el servidor puede diagnosticarlo.");
        }
        else
        {
            var template = MetadataText(gguf.Metadata, "tokenizer.chat_template");
            Console.WriteLine($"Chat template: {(string.IsNullOrWhiteSpace(template) ? "no declarado" : "disponible")}");
        }

        return 0;
    }

    private async Task<int> RunOneShotAsync(IReadOnlyList<string> rest)
    {
        if (rest.Count == 0)
        {
            throw new TerminalException("Falta el mensaje. Ejemplo: preguntar \"Hola\"");
        }

        string? selector = null;
        string prompt;
        if (rest.Count > 1 && catalog.TryResolve(rest[0], out _))
        {
            selector = rest[0];
            prompt = string.Join(" ", rest.Skip(1));
        }
        else
        {
            prompt = string.Join(" ", rest);
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new TerminalException("El mensaje está vacío.");
        }

        var model = ResolveModel(selector);
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += handler;
        try
        {
            await using var session = new LlamaServerSession(config, model);
            Console.WriteLine($"[1/2] Cargando {model.Name}...");
            await session.StartAsync(cancellation.Token);
            Console.WriteLine("[2/2] Consultando el agente local...");
            Console.ForegroundColor = ConsoleColor.DarkBlue;
            Console.Write("IA27> ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            using var notice = new GenerationNotice();
            await AskWithNetPermissionAsync(session, prompt, piece =>
            {
                notice.OnToken();
                lock (GenerationNotice.Sync)
                {
                    Console.Write(piece);
                }
                return Task.CompletedTask;
            }, cancellation.Token);
            Console.WriteLine();
            Console.WriteLine();
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }

    private async Task<int> RunAgentAsync(string? selector)
    {
        var requestedModel = selector ?? config.SelectedModel;

        if (string.IsNullOrWhiteSpace(requestedModel))
        {
            requestedModel = await SelectOrDownloadModel();
            if (requestedModel is null)
            {
                return 1;
            }
            config.SelectedModel = requestedModel;
        }

        var firstBanner = !noBanner;

        while (true)
        {
            ModelDescriptor model;
            try
            {
                model = ResolveModel(requestedModel);
            }
            catch (TerminalException error)
            {
                Console.Error.WriteLine(error.Message);
                return 1;
            }

            if (firstBanner)
            {
                PrintBanner(model);
                firstBanner = false;
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine($"Cambiando a: {model.Name}");
            }

            using var cancellation = new CancellationTokenSource();
            var cancelState = new ConsoleCancelState();
            ConsoleCancelEventHandler handler = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                if (cancelState.Generation is { } activeGeneration)
                {
                    activeGeneration.Cancel();
                }
                else
                {
                    cancellation.Cancel();
                }
            };
            Console.CancelKeyPress += handler;
            try
            {
                await using var session = new LlamaServerSession(config, model);
                Console.WriteLine("Cargando el modelo; la primera carga puede tardar...");
                await session.StartAsync(cancellation.Token);
                Console.WriteLine("Listo. Escribe /help para ver los comandos del agente. Ctrl+C detiene la respuesta en curso.");
                Console.WriteLine();
                if (config.NetEnabled)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write("[INTRANET] ¿Permitir que el agente busque en internet automáticamente en esta sesión? (s/n): ");
                    Console.ForegroundColor = ConsoleColor.White;
                    var netAnswer = Console.ReadLine()?.Trim().ToLowerInvariant();
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    var allowAuto = netAnswer is "s" or "si" or "sí" or "y" or "yes";
                    session.NetAutoAllowed = allowAuto;
                    Console.ForegroundColor = allowAuto ? ConsoleColor.Gray : ConsoleColor.DarkGray;
                    Console.WriteLine(allowAuto
                        ? "  Internet autorizado para esta sesión. El agente buscará automáticamente cuando necesite datos actuales."
                        : "  Internet denegado. El agente usará solo su conocimiento local.");
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine();
                }

                var action = await RunAgentLoopAsync(session, cancellation.Token, cancelState);
                if (action.SwitchTo is not null)
                {
                    requestedModel = action.SwitchTo;
                    continue;
                }

                return 0;
            }
            catch (OperationCanceledException)
            {
                return 130;
            }
            catch (TerminalException error)
            {
                Console.Error.WriteLine($"Error: {error.Message}");
                return 1;
            }
            finally
            {
                Console.CancelKeyPress -= handler;
            }
        }
    }

    private async Task<SessionAction> RunAgentLoopAsync(LlamaServerSession session, CancellationToken cancellationToken, ConsoleCancelState cancelState)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("tú> ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            var input = Console.ReadLine();
            if (input is null)
            {
                return new SessionAction(true, null);
            }

            input = input.Trim();
            if (input.Length == 0)
            {
                continue;
            }

            if (input.StartsWith('/'))
            {
                var action = await HandleAgentCommandAsync(session, input);
                if (action is not null)
                {
                    return action;
                }

                continue;
            }

            using var turn = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cancelState.Generation = turn;
            using var notice = new GenerationNotice();
            try
            {
                Console.ForegroundColor = ConsoleColor.DarkBlue;
                Console.Write("IA27> ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                await AskWithNetPermissionAsync(session, input, piece =>
                {
                    notice.OnToken();
                    lock (GenerationNotice.Sync)
                    {
                        Console.Write(piece);
                    }
                    return Task.CompletedTask;
                }, turn.Token);
                Console.WriteLine();
                Console.WriteLine();
            }
            catch (OperationCanceledException) when (turn.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine();
                Console.WriteLine("[generación detenida]");
            }
            catch (TerminalException error)
            {
                Console.Error.WriteLine($"Error: {error.Message}");
            }
            finally
            {
                cancelState.Generation = null;
            }
        }
    }

    private async Task<SessionAction?> HandleAgentCommandAsync(LlamaServerSession session, string input)
    {
        var separator = input.IndexOf(' ');
        var command = (separator < 0 ? input : input[..separator]).ToLowerInvariant();
        var argument = separator < 0 ? string.Empty : input[(separator + 1)..].Trim();
        switch (command)
        {
            case "/exit":
            case "/quit":
            case "/salir":
                return new SessionAction(true, null);
            case "/help":
            case "/ayuda":
                Console.WriteLine("/help  /clear  /history  /system [texto]  /modelos  /cambiar  /use <selector>  /descargar  /temp [valor]  /rp [valor]  /topp [valor]  /tokens [valor]  /net [on|off]  /exit");
                Console.WriteLine("Ctrl+C detiene la respuesta en curso; /tokens 512 produce respuestas más cortas.");
                Console.WriteLine("Herramientas del agente: puede leer archivos/carpetas ([[READ]]), ejecutar comandos PowerShell ([[CMD]]) y crear archivos ([[WRITE]]); ejecución y escritura siempre piden tu permiso (s/n).");
                break;
            case "/clear":
            case "/limpiar":
                session.ClearHistory();
                Console.WriteLine("Historial limpiado.");
                break;
            case "/history":
            case "/historial":
                PrintHistory(session);
                break;
            case "/system":
            case "/sistema":
                if (argument.Length == 0)
                {
                    Console.WriteLine(session.History.FirstOrDefault(message => message.Role == "system")?.Content ?? config.SystemPrompt);
                }
                else
                {
                    session.SetSystemPrompt(argument);
                    Console.WriteLine("Instrucción de sistema actualizada.");
                }
                break;
            case "/model":
            case "/modelo":
            case "/modelos":
            case "/models":
                if (argument.Length == 0)
                {
                    var allModels = catalog.List();
                    Console.ForegroundColor = ConsoleColor.DarkBlue;
                    Console.WriteLine("//== MODELOS INSTALADOS =================================================//");
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    for (var modelIndex = 0; modelIndex < allModels.Count; modelIndex++)
                    {
                        var marker = allModels[modelIndex].Name == session.Model.Name ? " ← actual" : "";
                        Console.ForegroundColor = marker.Length > 0 ? ConsoleColor.White : ConsoleColor.Cyan;
                        Console.WriteLine($"  {modelIndex + 1}. {allModels[modelIndex].Name} ({FormatBytes(allModels[modelIndex].SizeBytes)}){marker}");
                    }
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine();
                    Console.Write("  Escribe un número para cambiar, /descargar para bajar más, o Enter para continuar: ");
                    Console.ForegroundColor = ConsoleColor.White;
                    var modelChoice = Console.ReadLine()?.Trim();
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    if (string.IsNullOrWhiteSpace(modelChoice))
                    {
                        Console.WriteLine();
                        break;
                    }

                    if (modelChoice.Equals("/descargar", StringComparison.OrdinalIgnoreCase))
                    {
                        var downloadedModel = await DownloadAndSelect();
                        if (downloadedModel is not null)
                        {
                            return new SessionAction(false, downloadedModel);
                        }
                        break;
                    }

                    if (int.TryParse(modelChoice, out var modelNumber) && modelNumber > 0 && modelNumber <= allModels.Count)
                    {
                        return new SessionAction(false, allModels[modelNumber - 1].Name);
                    }

                    return new SessionAction(false, modelChoice);
                }
                else
                {
                    return new SessionAction(false, argument);
                }
            case "/use":
            case "/usar":
            case "/cambiar":
            case "/switch":
                if (argument.Length == 0)
                {
                    var switchModels = catalog.List();
                    Console.ForegroundColor = ConsoleColor.DarkBlue;
                    Console.WriteLine("//== CAMBIAR MODELO =====================================================//");
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    for (var switchIndex = 0; switchIndex < switchModels.Count; switchIndex++)
                    {
                        var switchMarker = switchModels[switchIndex].Name == session.Model.Name ? " ← actual" : "";
                        Console.ForegroundColor = switchMarker.Length > 0 ? ConsoleColor.White : ConsoleColor.Cyan;
                        Console.WriteLine($"  {switchIndex + 1}. {switchModels[switchIndex].Name}{switchMarker}");
                    }
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.Write("  Número del modelo: ");
                    Console.ForegroundColor = ConsoleColor.White;
                    var switchChoice = Console.ReadLine()?.Trim();
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    if (int.TryParse(switchChoice, out var switchNumber) && switchNumber > 0 && switchNumber <= switchModels.Count)
                    {
                        return new SessionAction(false, switchModels[switchNumber - 1].Name);
                    }

                    if (!string.IsNullOrWhiteSpace(switchChoice))
                    {
                        return new SessionAction(false, switchChoice);
                    }
                }
                else
                {
                    return new SessionAction(false, argument);
                }
                break;
            case "/descargar":
            case "/download":
                var downloaded = await DownloadAndSelect();
                if (downloaded is not null)
                {
                    return new SessionAction(false, downloaded);
                }
                break;
            case "/temp":
            case "/temperatura":
                if (double.TryParse(argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var temperature))
                {
                    session.Temperature = Math.Clamp(temperature, 0, 2);
                }
                Console.WriteLine($"Temperatura: {session.Temperature:0.00}");
                break;
            case "/rp":
            case "/repeat-penalty":
                if (double.TryParse(argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var repeatPenalty))
                {
                    session.RepeatPenalty = Math.Clamp(repeatPenalty, 1, 2);
                }
                Console.WriteLine($"Penalización de repetición: {session.RepeatPenalty:0.00}");
                break;
            case "/topp":
            case "/top-p":
                if (double.TryParse(argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var topP))
                {
                    session.TopP = Math.Clamp(topP, 0.1, 1);
                }
                Console.WriteLine($"Top-P: {session.TopP:0.00}");
                break;
            case "/net":
            case "/internet":
                if (argument.Length > 0)
                {
                    session.SetNetEnabled(argument is "on" or "si" or "sí" or "yes" or "true");
                    try
                    {
                        config.Save();
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
                Console.WriteLine(config.NetEnabled
                    ? "Internet: on — el modelo pedirá tu permiso antes de cada búsqueda."
                    : "Internet: off — el modelo responderá solo con su conocimiento local.");
                break;
            case "/tokens":
            case "/max-tokens":
                if (int.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxTokens))
                {
                    session.MaxTokens = Math.Clamp(maxTokens, 1, 131_072);
                }
                Console.WriteLine($"Tokens máximos: {session.MaxTokens}");
                break;
            default:
                Console.WriteLine("Comando desconocido. Usa /help.");
                break;
        }

        return null;
    }

    private async Task<string?> SelectOrDownloadModel()
    {
        var models = catalog.List();
        if (models.Count == 1)
        {
            return models[0].Name;
        }

        if (models.Count > 1)
        {
            Console.ForegroundColor = ConsoleColor.DarkBlue;
            Console.WriteLine("//== MODELOS DISPONIBLES ================================================//");
            Console.ForegroundColor = ConsoleColor.Cyan;
            for (var index = 0; index < models.Count; index++)
            {
                Console.WriteLine($"  {index + 1}. {models[index].Name} ({FormatBytes(models[index].SizeBytes)})");
            }

            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine();
            Console.Write("  Escribe /descargar para ver más modelos agente o elige un número (Enter = 1): ");
            Console.ForegroundColor = ConsoleColor.White;
            var input = Console.ReadLine()?.Trim();
            Console.ForegroundColor = ConsoleColor.Cyan;
            if (string.IsNullOrWhiteSpace(input) || input == "1" && models.Count > 0)
            {
                return models[0].Name;
            }

            if (input.Equals("/descargar", StringComparison.OrdinalIgnoreCase) || input.Equals("descargar", StringComparison.OrdinalIgnoreCase))
            {
                return await DownloadAndSelect();
            }

            if (int.TryParse(input, out var selection) && selection > 0 && selection <= models.Count)
            {
                return models[selection - 1].Name;
            }

            return models[0].Name;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("  No hay modelos instalados.");
        Console.ForegroundColor = ConsoleColor.Cyan;
        return await DownloadAndSelect();
    }

    private async Task<string?> DownloadAndSelect()
    {
        Console.ForegroundColor = ConsoleColor.DarkBlue;
        Console.WriteLine("//== MODELOS AGENTE DISPONIBLES PARA DESCARGAR ==========================//");
        Console.ForegroundColor = ConsoleColor.Cyan;

        var available = ModelRegistry.Models.ToList();
        for (var index = 0; index < available.Count; index++)
        {
            var info = available[index];
            var downloaded = info.Url == "local" || File.Exists(Path.Combine(config.ModelDirectory, info.FileName));
            var status = downloaded ? "[instalado]" : $"[{info.SizeGb:0.0} GB]";
            Console.ForegroundColor = downloaded ? ConsoleColor.Gray : ConsoleColor.Cyan;
            Console.WriteLine($"  {index + 1}. {info.Name,-35} {status,-12} {info.Strengths}");
        }

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine();
        Console.Write("  Escribe el número del modelo a descargar/usar (0 = cancelar, Enter = 1): ");
        Console.ForegroundColor = ConsoleColor.White;
        var input = Console.ReadLine()?.Trim();
        Console.ForegroundColor = ConsoleColor.Cyan;
        if (string.IsNullOrWhiteSpace(input))
        {
            input = "1";
        }

        if (!int.TryParse(input, out var choice) || choice < 0 || choice > available.Count)
        {
            Console.WriteLine("  Selección inválida.");
            return null;
        }

        if (choice == 0)
        {
            return null;
        }

        var selected = available[choice - 1];
        if (selected.Url == "local" || File.Exists(Path.Combine(config.ModelDirectory, selected.FileName)))
        {
            return selected.FileName;
        }

        Console.WriteLine();
        Console.WriteLine($"  Vas a descargar {selected.Name} ({selected.SizeGb:0.0} GB).");
        Console.Write("  Confirmar? (s/n): ");
        Console.ForegroundColor = ConsoleColor.White;
        var confirm = Console.ReadLine()?.Trim().ToLowerInvariant();
        Console.ForegroundColor = ConsoleColor.Cyan;
        if (confirm is not ("s" or "si" or "sí" or "y" or "yes"))
        {
            return null;
        }

        try
        {
            Console.WriteLine();
            using var cancellation = new CancellationTokenSource();
            await ModelRegistry.DownloadAsync(config.ModelDirectory, selected, cancellation.Token);
            Console.WriteLine($"  Modelo {selected.Name} instalado en: {config.ModelDirectory}");
            return selected.FileName;
        }
        catch (Exception error)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  Error descargando: {error.Message}");
            Console.ForegroundColor = ConsoleColor.Cyan;
            return null;
        }
    }

    private async Task<int> RunDownloadMenu(IReadOnlyList<string> rest)
    {
        if (rest.Count > 0)
        {
            var id = rest[0].ToLowerInvariant();
            var info = ModelRegistry.Find(id);
            if (info is null)
            {
                Console.WriteLine("Modelos disponibles:");
                foreach (var model in ModelRegistry.Models)
                {
                    var downloaded = model.Url == "local" || File.Exists(Path.Combine(config.ModelDirectory, model.FileName));
                    Console.WriteLine($"  {model.Id,-15} {(downloaded ? "[instalado]" : model.SizeGb.ToString("0.0") + " GB")} {model.Name}");
                }
                return 0;
            }

            Console.WriteLine($"Descargando {info.Name} ({info.SizeGb:0.0} GB)...");
            await ModelRegistry.DownloadAsync(config.ModelDirectory, info, CancellationToken.None);
            Console.WriteLine("Descarga completa.");
            return 0;
        }

        var selected = await DownloadAndSelect();
        return selected is null ? 1 : 0;
    }

    private async Task<int> RunServerAsync(string? selector)

    {
        var model = ResolveModel(selector);
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += handler;
        try
        {
            await using var session = new LlamaServerSession(config, model);
            Console.WriteLine($"Cargando {model.Name}...");
            await session.StartAsync(cancellation.Token);
            Console.WriteLine($"Servidor local activo: {session.BaseUrl}");
            Console.WriteLine($"API de chat: {session.BaseUrl}/v1/chat/completions");
            Console.WriteLine("Ctrl+C para detenerlo.");
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation.Token);
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (TerminalException error)
        {
            Console.Error.WriteLine($"Error: {error.Message}");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }

    private static readonly Regex NetRequestPattern = new(@"\[\[NET\]\]\s*(?<query>[^\r\n]*)", RegexOptions.Compiled);
    private static readonly Regex NetRefusalPattern = new(@"(no\s+tengo?\s+(?:la\s+)?capacidad\s+de\s+navegar|no\s+puedo\s+navegar|no\s+puedo\s+acceder\s+(?:a\s+)?(?:internet|la\s+web|wikipedia)|no\s+tengo?\s+(?:acceso|conexión)\s+(?:a\s+)?(?:internet|la\s+web)|sin\s+(?:acceso|conexión)\s+a\s+internet|no\s+puedo\s+buscar\s+en\s+(?:internet|la\s+web|la\s+red)|no\s+puedo\s+(?:realizar|hacer)\s+b[úu]squedas|no\s+tengo?\s+(?:la\s+)?capacidad\s+de\s+(?:buscar|realizar\s+b[úu]squedas|navegar|acceder)|no\s+tengo?\s+internet|no\s+puedo\s+proporcionar\s+informaci[oó]n\s+(?:en\s+tiempo\s+real|actual)|no\s+puedo\s+consultar\s+(?:fuentes|internet|la\s+web)|no\s+puedo\s+verificar\s+informaci[oó]n\s+actual|como\s+(?:modelo|asistente)\s+(?:de\s+)?(?:ia|inteligencia\s+artificial|lenguaje)[^.]{0,80}internet)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ExplicitSearchIntent = new(@"^\s*(?:busca|b[úu]scame|buscar|investiga|investigar|googlea|consulta(?:r)?|averigua|averiguar|mira|mirar)\b[\s,:\-]*(?<query>.+?)\s*(?:en\s+(?:internet|la\s+web|la\s+red|google|wikipedia|la\s+wiki))?[.\s]*$|^\s*(?<query>.+?)\s+en\s+(?:internet|la\s+web|la\s+red|google|wikipedia)\s*[.\s]*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static bool TryExtractExplicitSearchQuery(string prompt, out string query)
    {
        query = string.Empty;
        var match = ExplicitSearchIntent.Match(prompt);
        if (!match.Success)
        {
            return false;
        }

        var candidate = match.Groups["query"].Success ? match.Groups["query"].Value : match.Groups["query2"].Value;
        candidate = Regex.Replace(candidate, @"^\s*en\s+(?:internet|la\s+web|la\s+red|google|wikipedia|la\s+wiki)\s+(?:sobre\s+)?", string.Empty, RegexOptions.IgnoreCase);
        candidate = Regex.Replace(candidate, @"^\s*(?:informaci[oó]n|info|datos|detalles)\s+(?:sobre|de|del|acerca\s+de)\s+", string.Empty, RegexOptions.IgnoreCase).Trim(' ', '.', ',', ':', '-', '?', '¿', '!', '¡');
        candidate = Regex.Replace(candidate, @"^sobre\s+", string.Empty, RegexOptions.IgnoreCase).Trim();
        if (candidate.Length < 3 || candidate.Length > 120)
        {
            return false;
        }

        query = candidate;
        return true;
    }
    // ===== Herramientas de agente: READ / CMD / WRITE =====

    private static readonly Regex ReadRequestPattern = new(@"\[\[READ\]\]\s*(?<path>[^\r\n]+)", RegexOptions.Compiled);
    private static readonly Regex CmdRequestPattern = new(@"\[\[CMD\]\]\s*(?<cmd>[^\r\n]+)", RegexOptions.Compiled);
    private static readonly Regex WriteRequestPattern = new(@"\[\[WRITE\]\]\s*(?<path>[^\r\n]+?)\s*::\s*(?<body>[\s\S]*)$", RegexOptions.Compiled);
    private static readonly Regex DangerousCommandPattern = new(@"\b(?:format|diskpart|bcdedit|vssadmin)\b|remove-item[^\r\n]*-recurse[^\r\n]*-force|rm\s+-rf|del\s+/[sq]|rd\s+/s|shutdown|restart-computer|stop-computer|reg\s+delete|takeown|icacls[^\r\n]*/reset|clear-disk|initialize-disk|set-executionpolicy\s+unrestricted", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private sealed record ToolRequest(string Kind, string Argument, string? Body);

    private static ToolRequest? MatchToolRequest(string content)
    {
        var writeMatch = WriteRequestPattern.Match(content);
        if (writeMatch.Success)
        {
            return new ToolRequest("WRITE", writeMatch.Groups["path"].Value.Trim().Trim('"'), writeMatch.Groups["body"].Value);
        }

        var cmdMatch = CmdRequestPattern.Match(content);
        if (cmdMatch.Success)
        {
            return new ToolRequest("CMD", cmdMatch.Groups["cmd"].Value.Trim().Trim('"'), null);
        }

        var readMatch = ReadRequestPattern.Match(content);
        if (readMatch.Success)
        {
            return new ToolRequest("READ", readMatch.Groups["path"].Value.Trim().Trim('"'), null);
        }

        return null;
    }

    private static string StripToolMarkers(string content)
    {
        var cleaned = WriteRequestPattern.Replace(content, string.Empty);
        cleaned = CmdRequestPattern.Replace(cleaned, string.Empty);
        cleaned = ReadRequestPattern.Replace(cleaned, string.Empty);
        return cleaned.TrimEnd();
    }

    private static bool IsInsideSandbox(string path)
    {
        var roots = new[]
        {
            Path.GetFullPath(Environment.CurrentDirectory),
            Path.GetFullPath(AppContext.BaseDirectory),
            Path.GetFullPath(AppConfig.DefaultModelDirectory)
        };
        foreach (var root in roots)
        {
            if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ResolveToolPath(string rawPath)
    {
        string path;
        try
        {
            path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(rawPath), Environment.CurrentDirectory);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new TerminalException($"Ruta inválida: {rawPath}");
        }

        return path;
    }

    private static string ExecuteReadTool(string rawPath)
    {
        var path = ResolveToolPath(rawPath);
        if (Directory.Exists(path))
        {
            var builder = new StringBuilder();
            builder.AppendLine($"Contenido de la carpeta {path}:");
            var count = 0;
            foreach (var directory in Directory.EnumerateDirectories(path))
            {
                if (++count > 150) { builder.AppendLine("... (lista truncada)"); break; }
                builder.AppendLine($"  [carpeta] {Path.GetFileName(directory)}");
            }
            foreach (var file in Directory.EnumerateFiles(path))
            {
                if (++count > 150) { builder.AppendLine("... (lista truncada)"); break; }
                var info = new FileInfo(file);
                builder.AppendLine($"  {Path.GetFileName(file)} ({info.Length:N0} bytes)");
            }

            return count == 0 ? $"La carpeta {path} está vacía." : builder.ToString().TrimEnd();
        }

        if (File.Exists(path))
        {
            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                throw new TerminalException($"No se pudo leer el archivo: {error.Message}");
            }

            if (text.Length > 4000)
            {
                text = text[..4000] + "\n... [contenido truncado: archivo más largo que 4000 caracteres]";
            }

            return $"Contenido de {path}:\n{text}";
        }

        throw new TerminalException($"No existe el archivo ni la carpeta: {path}");
    }

    private static async Task<string> ExecuteCmdToolAsync(string command, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            WorkingDirectory = Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);

        using var process = Process.Start(startInfo) ?? throw new TerminalException("No se pudo iniciar PowerShell.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return $"El comando excedió el límite de 30 segundos y fue detenido: {command}";
        }

        var output = (await outputTask).Trim();
        var error = (await errorTask).Trim();
        var builder = new StringBuilder();
        builder.AppendLine($"Comando ejecutado (carpeta {Environment.CurrentDirectory}): {command}");
        builder.AppendLine($"Código de salida: {process.ExitCode}");
        if (output.Length > 0)
        {
            builder.AppendLine("Salida:");
            builder.AppendLine(Truncate(output, 2000));
        }

        if (error.Length > 0)
        {
            builder.AppendLine("Errores:");
            builder.AppendLine(Truncate(error, 1000));
        }

        if (output.Length == 0 && error.Length == 0)
        {
            builder.AppendLine("(sin salida)");
        }

        return builder.ToString().TrimEnd();
    }

    private static string ExecuteWriteTool(string rawPath, string body)
    {
        var path = ResolveToolPath(rawPath);
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllText(path, body, new UTF8Encoding(false));
        return $"Archivo escrito: {path} ({new FileInfo(path).Length:N0} bytes).";
    }

    private bool ConfirmTool(string prompt, bool dangerous, CancellationToken cancellationToken)
    {
        Console.ForegroundColor = dangerous ? ConsoleColor.Red : ConsoleColor.Yellow;
        if (dangerous)
        {
            Console.WriteLine("[ADVERTENCIA] El comando coincide con un patrón potencialmente destructivo.");
        }

        Console.Write($"{(dangerous ? "[PELIGRO]" : "[SOLICITUD]")} {Truncate(prompt, 120)} ¿Permitir? (s/n): ");
        Console.ForegroundColor = ConsoleColor.White;
        var line = Console.ReadLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        cancellationToken.ThrowIfCancellationRequested();
        return line?.Trim().ToLowerInvariant() is "s" or "si" or "sí" or "y" or "yes";
    }

    private async Task<string> ExecuteToolWithPermissionAsync(ToolRequest tool, bool explicitByUser, CancellationToken cancellationToken)
    {
        string result;
        switch (tool.Kind)
        {
            case "READ":
                var readPath = ResolveToolPath(tool.Argument);
                var insideSandbox = IsInsideSandbox(readPath);
                if (!insideSandbox && !explicitByUser)
                {
                    Console.WriteLine();
                    if (!ConfirmTool($"El agente quiere LEER fuera del área de trabajo: {readPath}", dangerous: false, cancellationToken))
                    {
                        return "[SISTEMA · el usuario denegó la lectura del archivo]\nInforma al usuario de que no se pudo leer el archivo porque denegó el permiso, sin inventar su contenido.";
                    }
                }

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkBlue;
                Console.Write("[herramienta: READ] ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                result = ExecuteReadTool(tool.Argument);
                Console.WriteLine($"leyendo {Truncate(readPath, 60)}... listo.");
                break;
            case "CMD":
                var dangerous = DangerousCommandPattern.IsMatch(tool.Argument);
                Console.WriteLine();
                if (!ConfirmTool($"El agente quiere EJECUTAR en PowerShell: {tool.Argument}", dangerous, cancellationToken))
                {
                    return "[SISTEMA · el usuario denegó la ejecución del comando]\nInforma al usuario de que la acción fue denegada; no inventes ningún resultado del comando.";
                }

                Console.ForegroundColor = ConsoleColor.DarkBlue;
                Console.Write("[herramienta: CMD] ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                result = await ExecuteCmdToolAsync(tool.Argument, cancellationToken);
                Console.WriteLine("ejecutando... listo.");
                break;
            case "WRITE":
                var writePath = ResolveToolPath(tool.Argument);
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine($"  Contenido ({(tool.Body ?? string.Empty).Length} caracteres): {Truncate((tool.Body ?? string.Empty).Replace("\r", " ").Replace("\n", " ⏎ "), 160)}");
                if (!ConfirmTool($"El agente quiere ESCRIBIR el archivo: {writePath}", dangerous: false, cancellationToken))
                {
                    return "[SISTEMA · el usuario denegó la escritura del archivo]\nInforma al usuario de que la escritura fue denegada; no digas que el archivo se creó.";
                }

                Console.ForegroundColor = ConsoleColor.DarkBlue;
                Console.Write("[herramienta: WRITE] ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                result = ExecuteWriteTool(tool.Argument, tool.Body ?? string.Empty);
                Console.WriteLine("escribiendo... listo.");
                break;
            default:
                return string.Empty;
        }

        return $"[SISTEMA · resultado de herramienta {(explicitByUser ? "pedida por el usuario" : "iniciada por el agente")}]\n{result}\nUsa este resultado real para responder al usuario; no inventes datos y no vuelvas a llamar a la misma herramienta si ya tienes la respuesta.";
    }

    // Intención explícita del usuario: "lee X", "ejecuta Y", "crea el archivo Z con..."
    private static readonly Regex ExplicitReadIntent = new(@"^\s*(?:lee|l[ée]eme|leer|abr[íi]|mostr[áa](?:me)?|muestra|revisa|analiza|resum[íi])\s+(?:(?:el|la|los|las|este|esta|un|una)\s+)?(?:archivo|fichero|carpeta|directorio|contenido\s+de)\s*:?\s*(?<path>.+?)\s*[.!?]*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ExplicitCmdIntent = new(@"^\s*(?:ejecuta(?:me)?|ejecutar|corre(?:me)?|correr|lanza(?:r)?)\s+(?:el\s+comando\s+)?(?<cmd>.+?)\s*[.!?]*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ExplicitWriteIntent = new(@"^\s*(?:crea(?:me)?|crear|escribe(?:me)?|escribir|genera(?:me)?)\s+(?:un\s+)?(?:el\s+)?archivo\s+(?<path>[^\s:]+(?::[^\s]*)?)(?:\s+(?:con|que\s+(?:diga|contenga|tenga)|con\s+el\s+contenido))\s*:?\s*(?<body>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static bool LooksLikePath(string value)
    {
        if (value.Contains("://", StringComparison.Ordinal) || value.Length < 2 || value.Length > 300)
        {
            return false;
        }

        if (value.Contains(':') || value.Contains('\\') || value.Contains('/'))
        {
            return true;
        }

        // Nombre simple con extensión o ruta que existe relativa a la carpeta actual
        return Regex.IsMatch(value, @"^[\w .\-()áéíóúñÁÉÍÓÚÑ]+\.\w{1,8}$");
    }

    private static bool TryExtractExplicitTool(string prompt, out ToolRequest tool)
    {
        tool = null!;
        var writeMatch = ExplicitWriteIntent.Match(prompt);
        if (writeMatch.Success && LooksLikePath(writeMatch.Groups["path"].Value))
        {
            tool = new ToolRequest("WRITE", writeMatch.Groups["path"].Value.Trim().Trim('"'), writeMatch.Groups["body"].Value);
            return true;
        }

        var readMatch = ExplicitReadIntent.Match(prompt);
        if (readMatch.Success && LooksLikePath(readMatch.Groups["path"].Value))
        {
            tool = new ToolRequest("READ", readMatch.Groups["path"].Value.Trim().Trim('"'), null);
            return true;
        }

        var cmdMatch = ExplicitCmdIntent.Match(prompt);
        if (cmdMatch.Success)
        {
            var candidate = cmdMatch.Groups["cmd"].Value.Trim();
            if (candidate.Length >= 2 && candidate.Length <= 300)
            {
                tool = new ToolRequest("CMD", candidate, null);
                return true;
            }
        }

        return false;
    }

    private static readonly HttpClient WebClient = CreateWebClient();

    private static HttpClient CreateWebClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) IA27Terminal/1.0");
        return client;
    }

    private async Task<string> AskWithNetPermissionAsync(LlamaServerSession session, string prompt, Func<string, Task> onToken, CancellationToken cancellationToken)
    {
        // Buffer anti-fugas: retiene la cola del stream para que el marcador [[NET]]
        // (y cualquier texto previo) nunca llegue a imprimirse en consola.
        var streamBuffer = new StringBuilder();
        Func<string, Task> bufferedToken = piece =>
        {
            streamBuffer.Append(piece);
            var cut = streamBuffer.ToString().LastIndexOf('[');
            if (cut < 0)
            {
                cut = Math.Max(0, streamBuffer.Length - 14);
            }
            else if (cut == 0 && streamBuffer.Length > 200 && (streamBuffer.Length < 2 || streamBuffer[1] != '['))
            {
                cut = 1;
            }

            if (cut <= 0)
            {
                return Task.CompletedTask;
            }

            var chunk = streamBuffer.ToString(0, cut);
            streamBuffer.Remove(0, cut);
            return onToken(chunk);
        };

        Task FlushBufferAsync()
        {
            if (streamBuffer.Length == 0)
            {
                return Task.CompletedTask;
            }

            var chunk = streamBuffer.ToString();
            streamBuffer.Clear();
            return onToken(chunk);
        }

        void DiscardBuffer() => streamBuffer.Clear();

        if (TryExtractExplicitTool(prompt, out var explicitTool))
        {
            try
            {
                var explicitResult = await ExecuteToolWithPermissionAsync(explicitTool, explicitByUser: true, cancellationToken);
                session.AddContextMessage(explicitResult + $"\nPedido original del usuario: \"{prompt}\"");
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkBlue;
                Console.Write("IA27> ");
                Console.ForegroundColor = ConsoleColor.Cyan;
            }
            catch (TerminalException toolError)
            {
                session.AddContextMessage($"[SISTEMA · herramienta fallida]\n{toolError.Message}\nInforma al usuario del error sin inventar resultados. Pedido original: \"{prompt}\"");
            }
        }
        else if (config.NetEnabled && TryExtractExplicitSearchQuery(prompt, out var explicitQuery))
        {
            var explicitAuthorized = session.NetAutoAllowed || AuthorizeNet(session, explicitQuery, cancellationToken);
            if (explicitAuthorized)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkBlue;
                Console.Write("[buscando en la web...] ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                var explicitResults = await SearchWebAsync(explicitQuery, cancellationToken);
                var explicitContext = explicitResults is not null
                    ? $"[SISTEMA · el usuario pidió explícitamente buscar en internet: \"{explicitQuery}\"]\n{explicitResults}\nEl usuario preguntó: \"{prompt}\". Responde usando estos datos reales de la web; cita la fuente cuando sea posible y NO inventes nada. Si los resultados no tratan del tema, dilo claramente."
                    : $"[SISTEMA · búsqueda fallida para \"{explicitQuery}\"]\nNo fue posible consultar internet ahora. Responde con tu propio conocimiento dejando claro que no se pudo verificar en internet y sin inventar datos.";
                session.AddContextMessage(explicitContext);
                Console.WriteLine("listo.");
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkBlue;
                Console.Write("IA27> ");
                Console.ForegroundColor = ConsoleColor.Cyan;
            }
        }

        var content = await session.AskAsync(prompt, bufferedToken, cancellationToken);
        var netRounds = 0;
        var toolRounds = 0;
        var originalPrompt = prompt;
        var searchContext = (string?)null;
        while (true)
        {
            var toolRequest = MatchToolRequest(content);
            if (toolRequest is not null)
            {
                if (toolRounds >= 5)
                {
                    DiscardBuffer();
                    session.ReplaceLastAssistant(StripToolMarkers(content));
                    return "Alcancé el límite de 5 usos de herramientas seguidos en este turno; reformulá el pedido o continúalo en otro mensaje.";
                }

                toolRounds++;
                DiscardBuffer();
                var cleanedTool = StripToolMarkers(content);
                session.ReplaceLastAssistant(cleanedTool);
                string toolContext;
                try
                {
                    toolContext = await ExecuteToolWithPermissionAsync(toolRequest, explicitByUser: false, cancellationToken);
                }
                catch (TerminalException toolError)
                {
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[herramienta: {toolRequest.Kind}] error: {toolError.Message}");
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    toolContext = $"[SISTEMA · herramienta {toolRequest.Kind} fallida]\n{toolError.Message}\nInforma al usuario del error; no inventes el resultado.";
                }

                session.AddContextMessage(toolContext);
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkBlue;
                Console.Write("IA27> ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                content = await session.AskContinueAsync(bufferedToken, cancellationToken);
                continue;
            }

            var match = NetRequestPattern.Match(content);
            var isRefusal = !match.Success && config.NetEnabled && NetRefusalPattern.IsMatch(content);
            if (isRefusal)
            {
                match = NetRequestPattern.Match("[[NET]] " + originalPrompt);
            }

            if (!match.Success)
            {
                await FlushBufferAsync();
                return content;
            }

            if (!config.NetEnabled || netRounds >= 2)
            {
                var denied = NetRequestPattern.Replace(content, string.Empty).TrimEnd();
                if (denied.Length > 0)
                {
                    session.ReplaceLastAssistant(denied);
                    DiscardBuffer();
                    return denied;
                }

                DiscardBuffer();
                return config.NetEnabled
                    ? "No fue posible completar la búsqueda en internet. Reformula la pregunta o intenta más tarde."
                    : "Internet está desactivado en esta terminal; actívalo con /net on.";
            }

            netRounds++;
            DiscardBuffer();
            var query = match.Groups["query"].Value.Trim();
            if (query.Length == 0 || query.Length > 120)
            {
                query = Truncate(originalPrompt, 100);
            }

            if (isRefusal)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[INTERCEPCIÓN] El modelo negó tener acceso a internet. Activando búsqueda con tu permiso...");
                Console.ForegroundColor = ConsoleColor.Cyan;
                session.ReplaceLastAssistant("");
            }
            else
            {
                var cleaned = NetRequestPattern.Replace(content, string.Empty).TrimEnd();
                session.ReplaceLastAssistant(cleaned);
            }

            Console.WriteLine();
            var authorized = session.NetAutoAllowed;
            if (!authorized)
            {
                authorized = AuthorizeNet(session, query, cancellationToken);
                if (!authorized)
                {
                    searchContext = $"[SISTEMA · el usuario denegó el acceso a internet para \"{query}\"]\nResponde con tu propio conocimiento, sin inventar datos actuales y sin mencionar este sistema.";
                    session.AddContextMessage(searchContext);
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.DarkBlue;
                    Console.Write("IA27> ");
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    content = await session.AskContinueAsync(bufferedToken, cancellationToken);
                    if (isRefusal && !string.IsNullOrWhiteSpace(content))
                    {
                        await FlushBufferAsync();
                        return content;
                    }
                    continue;
                }
            }

            Console.ForegroundColor = ConsoleColor.DarkBlue;
            Console.Write("[buscando en la web...] ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            var results = await SearchWebAsync(query, cancellationToken);
            searchContext = results is not null
                ? $"[SISTEMA · resultados de internet para \"{query}\"]\n{results}\nEl usuario preguntó: \"{originalPrompt}\". Usa estos datos reales para responder; cita la fuente cuando sea posible y no inventes nada."
                : $"[SISTEMA · búsqueda fallida para \"{query}\"]\nNo fue posible consultar internet ahora. Responde con tu propio conocimiento, sin inventar datos actuales y sin mencionar este sistema.";
            Console.WriteLine("listo.");

            session.AddContextMessage(searchContext);
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkBlue;
            Console.Write("IA27> ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            content = await session.AskContinueAsync(bufferedToken, cancellationToken);
            if (isRefusal && !string.IsNullOrWhiteSpace(content))
            {
                await FlushBufferAsync();
                return content;
            }
        }
    }

    private bool AuthorizeNet(LlamaServerSession session, string query, CancellationToken cancellationToken)
    {
        if (session.NetAutoAllowed)
        {
            return true;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write($"[SOLICITUD] El modelo quiere buscar en internet: \"{Truncate(query, 80)}\". Permitir? (s = sí / N = no): ");
        Console.ForegroundColor = ConsoleColor.White;
        var line = Console.ReadLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        cancellationToken.ThrowIfCancellationRequested();
        var answer = line?.Trim().ToLowerInvariant();
        var allowed = answer is "s" or "si" or "sí" or "y" or "yes";
        if (allowed)
        {
            session.NetAutoAllowed = true;
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("  Permiso recordado para esta sesión. El agente buscará automáticamente en adelante.");
            Console.ForegroundColor = ConsoleColor.Cyan;
        }
        return allowed;
    }

    private static async Task<string?> SearchWebAsync(string query, CancellationToken cancellationToken)
    {
        var ddg = await TryDuckDuckGoAsync(query, cancellationToken);
        if (!string.IsNullOrWhiteSpace(ddg))
        {
            return ddg;
        }

        return await TryWikipediaAsync(query, cancellationToken);
    }

    private static async Task<string?> TryDuckDuckGoAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            var url = "https://html.duckduckgo.com/html/?q=" + Uri.EscapeDataString(query);
            var html = await WebClient.GetStringAsync(url, cancellationToken);
            var linkMatches = Regex.Matches(html, "<a[^>]*class=\"result__a\"[^>]*href=\"([^\"]+)\"[^>]*>(.*?)</a>", RegexOptions.Singleline);
            if (linkMatches.Count == 0)
            {
                linkMatches = Regex.Matches(html, "<a[^>]*href=\"([^\"]+)\"[^>]*class=\"result__a\"[^>]*>(.*?)</a>", RegexOptions.Singleline);
            }

            if (linkMatches.Count == 0)
            {
                return null;
            }

            var snippetMatches = Regex.Matches(html, "<a[^>]*class=\"result__snippet\"[^>]*>(.*?)</a>", RegexOptions.Singleline);
            var builder = new StringBuilder();
            var count = 0;
            for (var index = 0; index < linkMatches.Count && count < 3; index++)
            {
                var title = StripHtml(linkMatches[index].Groups[2].Value);
                if (title.Length == 0)
                {
                    continue;
                }

                count++;
                builder.AppendLine($"{count}. {title}");
                if (index < snippetMatches.Count)
                {
                    var snippet = StripHtml(snippetMatches[index].Groups[1].Value);
                    if (snippet.Length > 0)
                    {
                        builder.AppendLine($"   {snippet}");
                    }
                }
            }

            return count > 0 ? builder.ToString().TrimEnd() : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private static async Task<string?> TryWikipediaAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            var url = "https://es.wikipedia.org/w/api.php?action=query&list=search&srsearch=" + Uri.EscapeDataString(query) + "&format=json&utf8=1&srlimit=3";
            var json = await WebClient.GetStringAsync(url, cancellationToken);
            using var document = JsonDocument.Parse(json);
            var items = document.RootElement.GetProperty("query").GetProperty("search");
            var builder = new StringBuilder();
            var count = 0;
            foreach (var item in items.EnumerateArray())
            {
                count++;
                var title = item.GetProperty("title").GetString() ?? string.Empty;
                var snippet = StripHtml(item.GetProperty("snippet").GetString() ?? string.Empty);
                builder.AppendLine($"{count}. Wikipedia: {title}");
                if (snippet.Length > 0)
                {
                    builder.AppendLine($"   {snippet}");
                }
            }

            return count > 0 ? builder.ToString().TrimEnd() : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException or NotSupportedException or JsonException or KeyNotFoundException or ArgumentException)
        {
            return null;
        }
    }

    private static string StripHtml(string value)
    {
        var text = Regex.Replace(value, "<[^>]+>", " ", RegexOptions.Singleline);
        text = WebUtility.HtmlDecode(text);
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private async Task<int> RunDoctorAsync()
    {
        var ok = true;
        Console.WriteLine("IA27 Terminal · diagnóstico");
        Console.WriteLine($"Configuración: {config.ConfigPath}");
        Console.WriteLine($"Python requerido: no");
        Console.WriteLine();
        Console.WriteLine(Check(config.ModelDirectory, Directory.Exists(config.ModelDirectory), "carpeta de modelos"));
        var models = catalog.List();
        if (models.Count == 0)
        {
            ok = false;
            Console.WriteLine("[X] no hay modelos GGUF en la carpeta");
        }
        else
        {
            Console.WriteLine($"[OK] modelos GGUF encontrados: {models.Count}");
            foreach (var model in models)
            {
                Console.WriteLine($"    {model.Name} ({FormatBytes(model.SizeBytes)})");
            }
        }

        ModelDescriptor? firstModel = null;
        if (models.Count > 0 && catalog.TryResolve(config.SelectedModel, out var selected) && selected is not null)
        {
            firstModel = selected;
        }
        else if (models.Count > 0)
        {
            firstModel = models[0];
        }

        if (firstModel is not null)
        {
            try
            {
                using var stream = File.OpenRead(firstModel.Path);
                Console.WriteLine($"[OK] modelo legible: {firstModel.Name} ({stream.Length:N0} bytes)");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                ok = false;
                Console.WriteLine($"[X] no se puede leer el modelo: {error.Message}");
            }
        }

        var runtime = RuntimeLocator.Find(config.RuntimeDirectory, config.ModelDirectory);
        if (runtime is null)
        {
            ok = false;
            Console.WriteLine("[X] no se encontró llama-server.exe");
            Console.WriteLine("    Configura: config set runtime-dir \"C:\\ruta\\llama_bin\"");
        }
        else
        {
            Console.WriteLine($"[OK] runtime: {runtime}");
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var version = await RuntimeLocator.ReadVersionAsync(runtime, timeout.Token);
                if (!string.IsNullOrWhiteSpace(version))
                {
                    Console.WriteLine($"[OK] versión: {version.Replace('\r', ' ').Replace('\n', ' ')}");
                }
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or SystemException)
            {
                Console.WriteLine($"[!] no se pudo leer la versión del runtime: {error.Message}");
            }
        }

        Console.WriteLine($"[INFO] contexto: {config.ContextSize}; tokens: {config.MaxTokens}; hilos: {config.Threads}; GPU layers: {config.GpuLayers}; net: {(config.NetEnabled ? "on" : "off")}");
        Console.WriteLine(ok ? "Resultado: OK" : "Resultado: revisar los puntos anteriores");
        return ok ? 0 : 1;
    }

    private int RunConfig(IReadOnlyList<string> rest)
    {
        var action = rest.Count == 0 ? "show" : rest[0].ToLowerInvariant();
        if (action is "show" or "mostrar")
        {
            Console.WriteLine($"Archivo:       {config.ConfigPath}");
            Console.WriteLine($"Modelos:       {config.ModelDirectory}");
            Console.WriteLine($"Runtime:       {config.RuntimeDirectory ?? "automático"}");
            Console.WriteLine($"Modelo:        {config.SelectedModel ?? "automático"}");
            Console.WriteLine($"Contexto:      {config.ContextSize}");
            Console.WriteLine($"Max tokens:    {config.MaxTokens}");
            Console.WriteLine($"Hilos CPU:     {config.Threads}");
            Console.WriteLine($"GPU layers:    {config.GpuLayers}");
            Console.WriteLine($"Temperatura:   {config.Temperature:0.00}");
            Console.WriteLine($"Repetic. pen.: {config.RepeatPenalty:0.00}");
            Console.WriteLine($"Top-P:         {config.TopP:0.00}");
            Console.WriteLine($"Chat template: {config.ChatTemplate}");
            Console.WriteLine($"Cache KV:      {config.CacheTypeK}/{config.CacheTypeV}");
            Console.WriteLine($"Internet:      {(config.NetEnabled ? "on (bajo autorización)" : "off")}");
            Console.WriteLine($"Espera carga:  {config.StartupTimeoutSeconds} s");
            Console.WriteLine($"System prompt: {Truncate(config.SystemPrompt, 100)}");
            return 0;
        }

        if (action == "path")
        {
            Console.WriteLine(config.ConfigPath);
            return 0;
        }

        if (action == "reset")
        {
            config.Reset();
            config.Save();
            Console.WriteLine("Configuración restablecida.");
            return 0;
        }

        if (action != "set" || rest.Count < 3)
        {
            throw new TerminalException("Uso: config set <clave> <valor> | config show | config reset | config path");
        }

        var key = rest[1].ToLowerInvariant().Replace('_', '-');
        var value = string.Join(" ", rest.Skip(2));
        SetConfigValue(key, value);
        config.Save();
        Console.WriteLine($"Configuración guardada: {key} = {value}");
        return 0;
    }

    private void SetConfigValue(string key, string value)
    {
        switch (key)
        {
            case "model-dir":
                config.ModelDirectory = value;
                break;
            case "runtime-dir":
                config.RuntimeDirectory = value.Equals("auto", StringComparison.OrdinalIgnoreCase) || value.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : value;
                break;
            case "model":
                config.SelectedModel = value;
                break;
            case "context":
                config.ContextSize = ParseInt(value, "context");
                break;
            case "max-tokens":
                config.MaxTokens = ParseInt(value, "max-tokens");
                break;
            case "threads":
                config.Threads = ParseInt(value, "threads");
                break;
            case "gpu-layers":
                config.GpuLayers = ParseInt(value, "gpu-layers");
                break;
            case "temperature":
                config.Temperature = ParseDouble(value, "temperature");
                break;
            case "repeat-penalty":
                config.RepeatPenalty = ParseDouble(value, "repeat-penalty");
                break;
            case "top-p":
                config.TopP = ParseDouble(value, "top-p");
                break;
            case "chat-template":
                config.ChatTemplate = value;
                break;
            case "cache-type-k":
                config.CacheTypeK = value;
                break;
            case "cache-type-v":
                config.CacheTypeV = value;
                break;
            case "net":
            case "internet":
                config.NetEnabled = value is "on" or "si" or "sí" or "yes" or "true";
                break;
            case "timeout":
                config.StartupTimeoutSeconds = ParseInt(value, "timeout");
                break;
            case "system-prompt":
                config.SystemPrompt = value;
                break;
            default:
                throw new TerminalException($"Clave desconocida: {key}");
        }
    }

    private ModelDescriptor ResolveModel(string? selector)
    {
        if (!catalog.TryResolve(selector ?? config.SelectedModel, out var model) || model is null)
        {
            if (string.IsNullOrWhiteSpace(selector ?? config.SelectedModel))
            {
                throw new TerminalException($"No se encontraron modelos GGUF en: {config.ModelDirectory}");
            }

            throw new TerminalException($"No se encontró el modelo '{selector ?? config.SelectedModel}'. Usa 'listar'.");
        }

        return model;
    }

    private void PrintBanner(ModelDescriptor model)
    {
        string[] logo =
        {
            "               ####               ",
            "      ##       ####       ##      ",
            "       ####    #####    ####      ",
            "         #### ##### ####         ",
            "           ##########            ",
            "  ##        ##########        ##  ",
            "    ####   ###########   ####    ",
            "      ###################        ",
            " ################################ ",
            "      ###################        ",
            "    ####   ###########   ####    ",
            "  ##        ##########        ##  ",
            "           ##########            ",
            "         #### ##### ####         ",
            "       ####    #####    ####     ",
            "      ##       ####       ##     ",
            "               ####              "
        };

        Console.ForegroundColor = ConsoleColor.DarkBlue;
        Console.WriteLine("//============================== IR3C5.CORE ==============================//");
        var logoWidth = logo.Max(line => line.Length);
        foreach (var line in logo)
        {
            var padding = new string(' ', (logoWidth - line.Length) / 2);
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.Write("  ");
            Console.WriteLine(padding + line);
        }

        Console.ForegroundColor = ConsoleColor.DarkBlue;
        Console.WriteLine("//============================== ESTALINGRADO CORP ======================//");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  INTRA-NET :: IA27 TERMINAL :: sistema local // canal seguro");
        Console.ForegroundColor = ConsoleColor.DarkBlue;
        Console.WriteLine("  ------------------------------------------------------------------------");
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine($"  modelo  : {model.Name}");
        Console.WriteLine($"  origen  : {config.ModelDirectory}");
        Console.WriteLine($"  motor   : llama.cpp local // net: {(config.NetEnabled ? "on" : "off")} // internet bajo autorización");
        Console.ForegroundColor = ConsoleColor.DarkBlue;
        Console.WriteLine("  ------------------------------------------------------------------------");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("  /help ayuda · /clear limpiar · /use <modelo> cambiar · /exit salir");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine();
    }

    private static void PrintHistory(LlamaServerSession session)
    {
        Console.WriteLine("Historial de la sesión:");
        for (var index = 0; index < session.History.Count; index++)
        {
            var message = session.History[index];
            Console.WriteLine($"[{index + 1}] {message.Role}: {Truncate(message.Content, 300)}");
        }
    }

    private static string Check(string value, bool success, string label)
    {
        return $"[{(success ? "OK" : "X")}] {label}: {value}";
    }

    private static string ReadOptionValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new TerminalException($"Falta el valor de {option}.");
        }

        index++;
        return args[index];
    }

    private static int ReadIntOption(string[] args, ref int index, string option)
    {
        return ParseInt(ReadOptionValue(args, ref index, option), option);
    }

    private static double ReadDoubleOption(string[] args, ref int index, string option)
    {
        return ParseDouble(ReadOptionValue(args, ref index, option), option);
    }

    private static int ParseInt(string value, string label)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            throw new TerminalException($"Valor inválido para {label}: {value}");
        }

        return result;
    }

    private static double ParseDouble(string value, string label)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        {
            throw new TerminalException($"Valor inválido para {label}: {value}");
        }

        return result;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes:N0} {units[unit]}" : $"{value:N2} {units[unit]}";
    }

    private static string Truncate(string value, int length)
    {
        if (value.Length <= length)
        {
            return value;
        }

        return value[..Math.Max(1, length - 3)] + "...";
    }

    private static object? MetadataValue(IReadOnlyDictionary<string, object?> metadata, string architecture, string suffix)
    {
        if (!string.IsNullOrWhiteSpace(architecture))
        {
            var key = $"{architecture}.{suffix}";
            if (metadata.TryGetValue(key, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static object? GeneralMetadataValue(IReadOnlyDictionary<string, object?> metadata, string key)
    {
        return metadata.TryGetValue(key, out var value) ? value : null;
    }

    private static string MetadataText(IReadOnlyDictionary<string, object?> metadata, string key)
    {
        return metadata.TryGetValue(key, out var value) ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty : string.Empty;
    }

    private static string? GeneralParameterCount(IReadOnlyDictionary<string, object?> metadata)
    {
        return metadata.TryGetValue("general.parameter_count", out var value) ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
    }

    private static string FormatParameterCount(string? value)
    {
        if (ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
        {
            return $"{count:N0} ({count / 1_000_000_000d:0.00} B)";
        }

        return value ?? "desconocido";
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "desconocido",
            GgufArrayInfo array => array.ToString(),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string QuantizationName(object? fileType, string fileName)
    {
        var value = fileType?.ToString();
        var name = value switch
        {
            "0" => "F32",
            "1" => "F16",
            "2" => "Q4_0",
            "3" => "Q4_1",
            "7" => "Q8_0",
            "8" => "Q5_0",
            "9" => "Q5_1",
            "10" => "Q2_K",
            "11" => "Q3_K_S",
            "12" => "Q3_K_M",
            "13" => "Q3_K_L",
            "14" => "Q4_K_S",
            "15" => "Q4_K_M",
            "16" => "Q5_K_S",
            "17" => "Q5_K_M",
            "18" => "Q6_K",
            "19" => "IQ2_XXS",
            "20" => "IQ2_XS",
            "21" => "Q3_K_XS",
            "22" => "IQ3_XXS",
            "23" => "IQ3_XS",
            "24" => "Q2_K_S",
            "25" => "IQ4_XS",
            "26" => "IQ4_NL",
            _ => string.Empty
        };
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        var upper = fileName.ToUpperInvariant();
        var candidates = new[] { "Q2_K", "Q3_K", "Q4_K", "Q5_K", "Q6_K", "Q8_0", "F16", "F32" };
        return candidates.FirstOrDefault(candidate => upper.Contains(candidate, StringComparison.Ordinal)) ?? "no declarada";
    }

    private sealed class ConsoleCancelState
    {
        public CancellationTokenSource? Generation { get; set; }
    }

    private sealed class GenerationNotice : IDisposable
    {
        public static readonly object Sync = new();
        private readonly System.Threading.Timer timer;
        private int tokens;
        private int shown;

        public GenerationNotice()
        {
            timer = new System.Threading.Timer(_ => OnTick());
            timer.Change(TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);
        }

        public void OnToken()
        {
            Interlocked.Increment(ref tokens);
        }

        public void Dispose()
        {
            timer.Dispose();
        }

        private void OnTick()
        {
            if (Interlocked.CompareExchange(ref shown, 1, 0) != 0)
            {
                return;
            }
            if (Volatile.Read(ref tokens) > 0)
            {
                return;
            }

            lock (Sync)
            {
                Console.WriteLine();
                Console.WriteLine("[aún generando; Ctrl+C para detener. Para respuestas más cortas usa /tokens 512]");
                Console.Write("IA27> ");
            }
        }
    }

    private sealed record SessionAction(bool Exit, string? SwitchTo);
}
