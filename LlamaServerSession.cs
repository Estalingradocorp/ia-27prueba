using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IaTerminal;

public sealed class ChatMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public sealed class LlamaServerSession : IAsyncDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly AppConfig config;
        private readonly ModelDescriptor model;
        private readonly List<ChatMessage> history;
        private readonly string logPath;
        private HttpClient? httpClient;
        private Process? process;
        private StreamWriter? logWriter;
        private int port;
        private bool netAutoAllowed;

    private const string NetPolicy = "ACCESO A INTERNET: está habilitado bajo autorización del usuario y TÚ SÍ PUEDES buscar en internet a través del sistema. NUNCA digas que no puedes navegar ni que no tienes acceso a internet: eso es falso. REGLA OBLIGATORIA: si el usuario pregunta por una entidad concreta (empresa, corporación, persona, producto, lugar, organización, evento) o por datos actuales o específicos (noticias, clima, precios, fechas, estadísticas) y no tienes certeza absoluta del dato, tu mensaje debe EMPEZAR directamente con [[NET]] seguido de la consulta breve (máximo 12 palabras), sin ningún texto, explicación ni comilla antes o después. Ejemplo de salida válida: \"[[NET]] Estalingrado Corp empresa\". El sistema buscará por ti y te llegará un mensaje con los resultados reales para responder con ellos. Si no llegan resultados o el usuario lo deniega, responde con tu propio conocimiento dejando claro que no está verificado. Ante la duda, busca: es preferible a dar un dato inventado.";

    public LlamaServerSession(AppConfig config, ModelDescriptor model)
    {
        this.config = config;
        this.model = model;
        history = new List<ChatMessage>
        {
            new() { Role = "system", Content = BuildSystemPrompt() }
        };
        logPath = Path.Combine(AppContext.BaseDirectory, "logs", $"llama-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");
    }

    public ModelDescriptor Model => model;
    public int Port => port;
    public string BaseUrl => $"http://127.0.0.1:{port}";
    public double Temperature { get; set; }
    public double RepeatPenalty { get; set; } = 1.1;
    public double TopP { get; set; } = 0.9;
    public int MaxTokens { get; set; }
    public bool NetAutoAllowed { get; set; }
    public IReadOnlyList<ChatMessage> History => history;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (process is not null)
        {
            return;
        }

        var executable = RuntimeLocator.Find(config.RuntimeDirectory, config.ModelDirectory);
        if (executable is null)
        {
            throw new TerminalException("No se encontró llama-server.exe. Configura la ruta con 'config set runtime-dir ...'.");
        }

        port = FindFreePort();
        httpClient = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromMinutes(30)
        };

        var logDirectory = Path.GetDirectoryName(logPath)!;
        Directory.CreateDirectory(logDirectory);
        logWriter = new StreamWriter(new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false))
        {
            AutoFlush = true
        };

        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        AddArgument(startInfo, "--model", model.Path);
        AddArgument(startInfo, "--host", "127.0.0.1");
        AddArgument(startInfo, "--port", port.ToString());
        AddArgument(startInfo, "--ctx-size", config.ContextSize.ToString());
        AddArgument(startInfo, "--threads", config.Threads.ToString());
        AddArgument(startInfo, "--n-gpu-layers", config.GpuLayers.ToString());
        AddArgument(startInfo, "--cache-type-k", config.CacheTypeK);
        AddArgument(startInfo, "--cache-type-v", config.CacheTypeV);
        AddArgument(startInfo, "--parallel", "1");
        if (!string.IsNullOrWhiteSpace(config.ChatTemplate))
        {
            AddArgument(startInfo, "--chat-template", config.ChatTemplate);
        }
        AddArgument(startInfo, "--no-warmup");
        AddArgument(startInfo, "--log-disable");

        process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, eventArgs) => WriteLog(eventArgs.Data, false);
        process.ErrorDataReceived += (_, eventArgs) => WriteLog(eventArgs.Data, true);
        process.Exited += (_, _) => WriteLog("El proceso llama-server terminó.", true);

        try
        {
            if (!process.Start())
            {
                throw new TerminalException("No se pudo iniciar llama-server.exe.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await WaitUntilReadyAsync(cancellationToken);
            Temperature = config.Temperature;
            RepeatPenalty = config.RepeatPenalty;
            TopP = config.TopP;
            MaxTokens = config.MaxTokens;
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task<string> AskAsync(string prompt, CancellationToken cancellationToken)
    {
        return await AskAsync(prompt, null, cancellationToken);
    }

    public async Task<string> AskAsync(string prompt, Func<string, Task>? onToken, CancellationToken cancellationToken)
    {
        if (httpClient is null)
        {
            throw new TerminalException("La sesión no está iniciada.");
        }

        var userMessage = new ChatMessage { Role = "user", Content = prompt };
        history.Add(userMessage);

        try
        {
            var content = await GenerateAsync(onToken, cancellationToken);
            history.Add(new ChatMessage { Role = "assistant", Content = content });
            return content;
        }
        catch
        {
            var index = history.IndexOf(userMessage);
            if (index >= 0 && history.Count > index && history[^1].Role != "assistant")
            {
                history.RemoveRange(index, history.Count - index);
            }

            throw;
        }
    }

    public async Task<string> AskContinueAsync(Func<string, Task>? onToken, CancellationToken cancellationToken)
    {
        if (httpClient is null)
        {
            throw new TerminalException("La sesión no está iniciada.");
        }

        var content = await GenerateAsync(onToken, cancellationToken);
        history.Add(new ChatMessage { Role = "assistant", Content = content });
        return content;
    }

    private async Task<string> GenerateAsync(Func<string, Task>? onToken, CancellationToken cancellationToken)
    {
        string? content = null;
        var attempts = new[]
        {
            (Temperature: Temperature, RepeatPenalty: RepeatPenalty, TopP: TopP),
            (Temperature: 0.65, RepeatPenalty: 1.05, TopP: 0.95),
            (Temperature: 0.3, RepeatPenalty: Math.Max(RepeatPenalty, 1.15), TopP: TopP)
        };
        for (var attempt = 0; attempt < attempts.Length && string.IsNullOrWhiteSpace(content); attempt++)
        {
            content = await SendStreamAsync(attempts[attempt].Temperature, attempts[attempt].RepeatPenalty, attempts[attempt].TopP, onToken, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new TerminalException("El modelo devolvió una respuesta vacía.");
        }

        return content;
    }

    private async Task<string> SendStreamAsync(double temperature, double repeatPenalty, double topP, Func<string, Task>? onToken, CancellationToken cancellationToken)
    {
        var request = new ChatCompletionRequest
        {
            Model = model.Id,
            Messages = BuildRequestMessages(),
            MaxTokens = MaxTokens,
            Temperature = temperature,
            RepeatPenalty = repeatPenalty,
            TopP = topP,
            Stream = true
        };
        var requestJson = JsonSerializer.Serialize(request, JsonOptions);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
        };
        using var response = await httpClient!.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new TerminalException($"llama-server respondió {(int)response.StatusCode}: {TrimForMessage(errorBody)}");
        }

        var builder = new StringBuilder();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            line = line.Trim();
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line[5..].TrimStart();
            if (data == "[DONE]")
            {
                break;
            }

            ChatChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<ChatChunk>(data, JsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            var piece = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
            if (string.IsNullOrEmpty(piece))
            {
                continue;
            }

            var degenerateStart = FindRepeatedTailStart(builder.ToString());
            if (degenerateStart >= 0)
            {
                builder.Length = degenerateStart;
                WriteLog("Cola repetida truncada por detección de degeneración.", true);
                break;
            }

            builder.Append(piece);
            if (onToken is not null)
            {
                await onToken(piece);
            }
        }

        return builder.ToString();
    }

    private static int FindRepeatedTailStart(string text)
    {
        if (text.Length < 48)
        {
            return -1;
        }

        var limit = Math.Min(text.Length, 240);
        var start = text.Length - limit;
        var counts = new Dictionary<char, int>();
        for (var index = text.Length - 1; index >= start; index--)
        {
            var character = text[index];
            if (char.IsWhiteSpace(character))
            {
                continue;
            }

            counts.TryGetValue(character, out var count);
            counts[character] = count + 1;
        }

        if (counts.Count == 0)
        {
            return -1;
        }

        var total = counts.Values.Sum();
        var dominant = counts.Aggregate((left, right) => left.Value >= right.Value ? left : right);
        if (dominant.Value < 24 || dominant.Value < (int)(total * 0.8))
        {
            return -1;
        }

        var position = text.Length - 1;
        while (position >= start && (char.IsWhiteSpace(text[position]) || text[position] == dominant.Key))
        {
            position--;
        }

        return position + 1;
    }

    public void ClearHistory()
    {
        history.Clear();
        history.Add(new ChatMessage { Role = "system", Content = BuildSystemPrompt() });
    }

    public void SetSystemPrompt(string systemPrompt)
    {
        config.SystemPrompt = systemPrompt;
        history.RemoveAll(message => message.Role == "system");
        history.Insert(0, new ChatMessage { Role = "system", Content = BuildSystemPrompt() });
    }

    public void SetNetEnabled(bool enabled)
    {
        config.NetEnabled = enabled;
        var system = history.FirstOrDefault(message => message.Role == "system");
        if (system is not null)
        {
            system.Content = BuildSystemPrompt();
        }
    }

    public void ReplaceLastAssistant(string content)
    {
        for (var index = history.Count - 1; index >= 0; index--)
        {
            if (history[index].Role == "assistant")
            {
                history[index].Content = content;
                return;
            }
        }
    }

    public void AddContextMessage(string content)
    {
        history.Add(new ChatMessage { Role = "user", Content = content });
    }

    private string BuildSystemPrompt()
    {
        return config.NetEnabled ? config.SystemPrompt + Environment.NewLine + Environment.NewLine + NetPolicy : config.SystemPrompt;
    }

    public async ValueTask DisposeAsync()
    {
        if (process is not null)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (SystemException)
            {
            }

            process.Dispose();
            process = null;
        }

        if (httpClient is not null)
        {
            httpClient.Dispose();
            httpClient = null;
        }

        if (logWriter is not null)
        {
            await logWriter.FlushAsync();
            logWriter.Dispose();
            logWriter = null;
        }
    }

    private async Task WaitUntilReadyAsync(CancellationToken cancellationToken)
    {
        if (httpClient is null || process is null)
        {
            throw new TerminalException("No se pudo preparar la sesión local.");
        }

        var deadline = DateTime.UtcNow.AddSeconds(config.StartupTimeoutSeconds);
        Exception? lastError = null;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                throw new TerminalException($"llama-server terminó durante el arranque. Log: {logPath}{Environment.NewLine}{ReadLogTail()}");
            }

            try
            {
                using var response = await httpClient.GetAsync("/health", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException error)
            {
                lastError = error;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastError = new TimeoutException("La comprobación de salud agotó el tiempo.");
            }

            await Task.Delay(500, cancellationToken);
        }

        var detail = lastError is null ? string.Empty : $" Última causa: {lastError.Message}";
        throw new TerminalException($"El modelo no estuvo listo en {config.StartupTimeoutSeconds} s.{detail} Log: {logPath}");
    }

    private List<ChatMessage> BuildRequestMessages()
    {
        var result = new List<ChatMessage>();
        var system = history.FirstOrDefault(message => message.Role == "system");
        if (system is not null)
        {
            result.Add(new ChatMessage { Role = system.Role, Content = system.Content });
        }

        var limit = Math.Max(8000, config.ContextSize * 3);
        var used = system?.Content.Length ?? 0;
        var selected = new List<ChatMessage>();
        for (var index = history.Count - 1; index >= 0; index--)
        {
            var message = history[index];
            if (message.Role == "system")
            {
                continue;
            }

            if (used + message.Content.Length > limit && selected.Count > 0)
            {
                break;
            }

            selected.Add(new ChatMessage { Role = message.Role, Content = message.Content });
            used += message.Content.Length;
        }

        selected.Reverse();
        result.AddRange(selected);
        return result;
    }

    private void WriteLog(string? text, bool error)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        try
        {
            logWriter?.WriteLine($"[{DateTime.Now:HH:mm:ss}] {(error ? "ERR " : "OUT ")}{text}");
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private string ReadLogTail()
    {
        try
        {
            var text = File.ReadAllText(logPath);
            return text.Length <= 2400 ? text : text[^2400..];
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void AddArgument(ProcessStartInfo startInfo, string argument, string? value = null)
    {
        startInfo.ArgumentList.Add(argument);
        if (value is not null)
        {
            startInfo.ArgumentList.Add(value);
        }
    }

    private static string TrimForMessage(string text)
    {
        var value = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return value.Length <= 600 ? value : value[..600];
    }

    private sealed class ChatCompletionRequest
    {
        public string Model { get; set; } = string.Empty;
        public List<ChatMessage> Messages { get; set; } = new();
        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }
        public double Temperature { get; set; }
        [JsonPropertyName("repeat_penalty")]
        public double RepeatPenalty { get; set; }
        [JsonPropertyName("top_p")]
        public double TopP { get; set; }
        public bool Stream { get; set; }
    }

    private sealed class ChatChunk
    {
        public List<ChunkChoice>? Choices { get; set; }
    }

    private sealed class ChunkChoice
    {
        public ChunkDelta? Delta { get; set; }
    }

    private sealed class ChunkDelta
    {
        public string? Content { get; set; }
    }
}

public static class RuntimeLocator
{
    public static string? Find(string? configuredDirectory, string modelDirectory)
    {
        foreach (var candidate in Candidates(configuredDirectory, modelDirectory))
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    public static IReadOnlyList<string> Candidates(string? configuredDirectory, string modelDirectory)
    {
        var result = new List<string>();
        var executableNames = new[] { "llama-server.exe", "llama-server" };
        var directories = new List<string>();

        if (!string.IsNullOrWhiteSpace(configuredDirectory))
        {
            AddCandidate(result, executableNames, configuredDirectory);
        }

        var environmentExecutable = Environment.GetEnvironmentVariable("IA27_LLAMA_SERVER");
        if (!string.IsNullOrWhiteSpace(environmentExecutable))
        {
            AddCandidate(result, executableNames, environmentExecutable);
        }

        directories.Add(Path.Combine(AppContext.BaseDirectory, "runtime"));
        directories.Add(AppContext.BaseDirectory);
        var cursor = new DirectoryInfo(Path.GetFullPath(modelDirectory));
        for (var level = 0; level < 5 && cursor is not null; level++)
        {
            directories.Add(Path.Combine(cursor.FullName, "entrenamiento", "llama_bin"));
            directories.Add(Path.Combine(cursor.FullName, "runtime"));
            cursor = cursor.Parent;
        }

        foreach (var directory in directories)
        {
            AddCandidate(result, executableNames, directory);
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathValue))
        {
            foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                AddCandidate(result, executableNames, directory.Trim().Trim('"'));
            }
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static async Task<string?> ReadVersionAsync(string executable, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("--version");
        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return null;
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = (await outputTask).Trim();
        var error = (await errorTask).Trim();
        return string.IsNullOrWhiteSpace(output) ? error : output;
    }

    private static void AddCandidate(List<string> result, IReadOnlyList<string> names, string path)
    {
        try
        {
            if (File.Exists(path) && names.Any(name => string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(Path.GetFullPath(path));
                return;
            }

            if (Directory.Exists(path))
            {
                foreach (var name in names)
                {
                    var candidate = Path.Combine(path, name);
                    if (File.Exists(candidate))
                    {
                        result.Add(Path.GetFullPath(candidate));
                    }
                }
            }
        }
        catch (ArgumentException)
        {
        }
        catch (NotSupportedException)
        {
        }
    }
}
