using System.Text.Json;
using System.Text.Json.Serialization;

namespace IaTerminal;

public sealed class AppConfig
{
    public const string DefaultModelDirectory = @"C:\Users\nicot\OneDrive\Documentos\modelos IA\models";
    public const string DefaultSystemPrompt = "Eres Atenea Omega Beta (IA-27), un agente de inteligencia artificial local. Responde en español, con precision y de forma practica. No afirmes haber ejecutado acciones externas que no se hayan proporcionado. Puedes ayudar a redactar, programar, analizar y organizar tareas.";

    public string ModelDirectory { get; set; } = DefaultModelDirectory;
    public string? RuntimeDirectory { get; set; }
    public string? SelectedModel { get; set; }
    public int ContextSize { get; set; } = 4096;
    public int MaxTokens { get; set; } = 2048;
    public int Threads { get; set; } = 4;
    public int GpuLayers { get; set; }
    public double Temperature { get; set; } = 0.7;
    public double RepeatPenalty { get; set; } = 1.1;
    public double TopP { get; set; } = 0.9;
    public string ChatTemplate { get; set; } = "chatml";
    public string CacheTypeK { get; set; } = "f16";
    public string CacheTypeV { get; set; } = "f16";
    public bool NetEnabled { get; set; } = true;
    public int StartupTimeoutSeconds { get; set; } = 180;
    public string SystemPrompt { get; set; } = DefaultSystemPrompt;

    [JsonIgnore]
    public string ConfigPath { get; private set; }

    public AppConfig(string? configPath)
    {
        ConfigPath = configPath ?? GetDefaultConfigPath();
    }

    public static AppConfig Load()
    {
        var config = new AppConfig(GetDefaultConfigPath());
        if (!File.Exists(config.ConfigPath))
        {
            config.Normalize();
            return config;
        }

        try
        {
            var json = File.ReadAllText(config.ConfigPath);
            var loaded = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (loaded is not null)
            {
                loaded.ConfigPath = config.ConfigPath;
                loaded.Normalize();
                return loaded;
            }
        }
        catch (IOException)
        {
        }
        catch (JsonException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        config.Normalize();
        return config;
    }

    public void Save()
    {
        Normalize();
        var directory = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(ConfigPath, json, new System.Text.UTF8Encoding(false));
    }

    public void Reset()
    {
        ModelDirectory = DefaultModelDirectory;
        RuntimeDirectory = null;
        SelectedModel = null;
        ContextSize = 4096;
        MaxTokens = 2048;
        Threads = 4;
        GpuLayers = 0;
        Temperature = 0.7;
        RepeatPenalty = 1.1;
        TopP = 0.9;
        ChatTemplate = "chatml";
        CacheTypeK = "f16";
        CacheTypeV = "f16";
        NetEnabled = true;
        StartupTimeoutSeconds = 180;
        SystemPrompt = DefaultSystemPrompt;
    }

    public void Normalize()
    {
        ModelDirectory = ExpandPath(ModelDirectory, DefaultModelDirectory);
        RuntimeDirectory = string.IsNullOrWhiteSpace(RuntimeDirectory)
            ? null
            : ExpandPath(RuntimeDirectory, AppContext.BaseDirectory);
        ContextSize = Math.Clamp(ContextSize, 256, 1_048_576);
        MaxTokens = Math.Clamp(MaxTokens, 1, 131_072);
        Threads = Math.Clamp(Threads, 1, 1024);
        GpuLayers = Math.Clamp(GpuLayers, 0, 10_000);
        Temperature = Math.Clamp(Temperature, 0, 2);
        RepeatPenalty = Math.Clamp(RepeatPenalty, 1, 2);
        TopP = Math.Clamp(TopP, 0.1, 1);
        ChatTemplate = string.IsNullOrWhiteSpace(ChatTemplate) ? "chatml" : ChatTemplate.Trim();
        CacheTypeK = string.IsNullOrWhiteSpace(CacheTypeK) ? "f16" : CacheTypeK.Trim();
        CacheTypeV = string.IsNullOrWhiteSpace(CacheTypeV) ? "f16" : CacheTypeV.Trim();
        StartupTimeoutSeconds = Math.Clamp(StartupTimeoutSeconds, 10, 3_600);
        if (string.IsNullOrWhiteSpace(SystemPrompt))
        {
            SystemPrompt = DefaultSystemPrompt;
        }
    }

    private static string ExpandPath(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Path.GetFullPath(Expand(fallback));
        }

        return Path.GetFullPath(Expand(value.Trim().Trim('"')));
    }

    private static string Expand(string value)
    {
        return Environment.ExpandEnvironmentVariables(value);
    }

    private static string GetDefaultConfigPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = AppContext.BaseDirectory;
        }

        return Path.Combine(root, "IA27Terminal", "config.json");
    }
}
