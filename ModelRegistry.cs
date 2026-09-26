using System.Net.Http.Headers;

namespace IaTerminal;

public sealed record AgentModelInfo(
    string Id,
    string Name,
    string Description,
    string Url,
    string FileName,
    double SizeGb,
    string Strengths)
{
    public override string ToString()
    {
        return $"{Name} ({SizeGb:0.0} GB) — {Description}";
    }
}

public static class ModelRegistry
{
    public static readonly IReadOnlyList<AgentModelInfo> Models = new[]
    {
        new AgentModelInfo(
            "ia27-atena",
            "IA27 Atenea Omega Beta",
            "Modelo actual (Qwen2.5-Coder base, español, código)",
            "local",
            "IA27-Atena-Omega-beta.gguf",
            4.36,
            "Código + español"),
        new AgentModelInfo(
            "qwen-instruct",
            "Qwen2.5-7B-Instruct",
            "Agente general con tool calling, español excelente",
            "https://huggingface.co/bartowski/Qwen2.5-7B-Instruct-GGUF/resolve/main/Qwen2.5-7B-Instruct-Q4_K_M.gguf",
            "Qwen2.5-7B-Instruct-Q4_K_M.gguf",
            4.68,
            "Agente general + español + herramientas"),
        new AgentModelInfo(
            "hermes-3",
            "Hermes-3-Llama-3.1-8B",
            "Especializado en function calling y tareas agénticas",
            "https://huggingface.co/bartowski/Hermes-3-Llama-3.1-8B-GGUF/resolve/main/Hermes-3-Llama-3.1-8B-Q4_K_M.gguf",
            "Hermes-3-Llama-3.1-8B-Q4_K_M.gguf",
            4.92,
            "Mejor para agentes puros"),
        new AgentModelInfo(
            "llama31",
            "Llama-3.1-8B-Instruct",
            "Function calling nativo, buen rendimiento general",
            "https://huggingface.co/bartowski/Meta-Llama-3.1-8B-Instruct-GGUF/resolve/main/Meta-Llama-3.1-8B-Instruct-Q4_K_M.gguf",
            "Meta-Llama-3.1-8B-Instruct-Q4_K_M.gguf",
            4.92,
            "Function calling nativo"),
        new AgentModelInfo(
            "mistral-v3",
            "Mistral-7B-Instruct-v0.3",
            "Rápido y eficiente, buen para uso general",
            "https://huggingface.co/bartowski/Mistral-7B-Instruct-v0.3-GGUF/resolve/main/Mistral-7B-Instruct-v0.3-Q4_K_M.gguf",
            "Mistral-7B-Instruct-v0.3-Q4_K_M.gguf",
            4.37,
            "Rápido + eficiente")
    };

    public static AgentModelInfo? Find(string id)
    {
        return Models.FirstOrDefault(model => string.Equals(model.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsDownloaded(string modelDirectory, AgentModelInfo info)
    {
        if (info.Url == "local")
        {
            return true;
        }

        return File.Exists(Path.Combine(modelDirectory, info.FileName));
    }

    public static async Task DownloadAsync(string modelDirectory, AgentModelInfo info, CancellationToken cancellationToken)
    {
        if (info.Url == "local")
        {
            throw new TerminalException("Este modelo ya está disponible localmente.");
        }

        var destination = Path.Combine(modelDirectory, info.FileName);
        if (File.Exists(destination))
        {
            throw new TerminalException($"El archivo ya existe: {destination}");
        }

        Directory.CreateDirectory(modelDirectory);
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("IA27Terminal/1.0");
        client.Timeout = TimeSpan.FromHours(2);

        Console.ForegroundColor = ConsoleColor.DarkBlue;
        Console.Write($"  Descargando {info.Name}... ");
        Console.ForegroundColor = ConsoleColor.Cyan;

        using var response = await client.GetAsync(info.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var totalBytes = response.Content.Headers.ContentLength ?? 0;

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true);

        var buffer = new byte[1024 * 256];
        long downloaded = 0;
        var lastReport = DateTime.MinValue;
        int bytesRead;
        while ((bytesRead = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            downloaded += bytesRead;
            if (totalBytes > 0 && DateTime.UtcNow - lastReport >= TimeSpan.FromSeconds(1))
            {
                lastReport = DateTime.UtcNow;
                var progress = (double)downloaded / totalBytes * 100;
                Console.Write($"\r  Descargando {info.Name}... {progress:F1}% ({downloaded / 1024.0 / 1024.0:F0} MB)   ");
            }
        }

        Console.WriteLine("\r  Descarga completa.                    ");
        Console.ForegroundColor = ConsoleColor.Cyan;
    }
}
