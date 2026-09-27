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
        // La ruta de modelos que el usuario acaba de fijar manda sobre la
        // resolución automática portable; si no, se perdería al guardar.
        var requestedModelDirectory = ModelDirectory;
        Normalize();
        if (!string.IsNullOrWhiteSpace(requestedModelDirectory))
        {
            var requested = requestedModelDirectory.Trim().Trim('"');
            if (Directory.Exists(Expand(requested)))
            {
                ModelDirectory = Path.GetFullPath(Expand(requested));
            }
        }

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
        // Modo pendrive: priorizar una carpeta "models" junto al ejecutable o una
        // carpeta de modelos en un nivel superior (../.. /Modelo), para que el
        // portable funcione sin importar la letra de unidad.
        ModelDirectory = ResolveModelDirectory(ModelDirectory);
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

    private static string ResolveModelDirectory(string? configured)
    {
        // 1) Carpeta "models" junto al ejecutable: es el layout portable canónico
        //    y debe ganar siempre sobre rutas absolutas de otra máquina.
        //    Solo si contiene al menos un .gguf (una carpeta vacía no debe tapar
        //    la ruta configurada).
        var portableModels = Path.Combine(AppContext.BaseDirectory, "models");
        if (Directory.Exists(portableModels) && ContainsGguf(portableModels))
        {
            return Path.GetFullPath(portableModels);
        }

        var configuredPath = string.IsNullOrWhiteSpace(configured)
            ? null
            : Path.GetFullPath(Expand(configured.Trim().Trim('"')));
        if (configuredPath is not null && Directory.Exists(configuredPath))
        {
            return configuredPath;
        }

        // 2) Buscar "Modelo"/"models"/"Modelos" hasta 3 niveles arriba del ejecutable.
        //    Permite portable en <raiz>\carpeta\publish y modelos en <raiz>\Modelo,
        //    sin depender de la letra de unidad.
        var cursor = new DirectoryInfo(AppContext.BaseDirectory);
        for (var level = 0; level < 3 && cursor?.Parent is not null; level++)
        {
            cursor = cursor.Parent;
            foreach (var name in new[] { "Modelo", "models", "Modelos", "modelos IA" })
            {
                var candidate = Path.Combine(cursor.FullName, name);
                if (Directory.Exists(candidate) && ContainsGguf(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        // 3) Sin candidatos: conservar la ruta configurada (o la por defecto)
        //    para que el diagnóstico muestre una ruta útil.
        return configuredPath ?? Path.GetFullPath(Expand(DefaultModelDirectory));
    }

    private static bool ContainsGguf(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*.gguf", SearchOption.AllDirectories).Any();
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
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
        // Modo pendrive: la configuración viaja con el ejecutable.
        // Si la carpeta del portable no es escribible, se cae a %APPDATA%.
        var portable = Path.Combine(AppContext.BaseDirectory, "config.json");
        try
        {
            var directory = Path.GetDirectoryName(portable)!;
            Directory.CreateDirectory(directory);
            if (IsWritable(directory))
            {
                return portable;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (NotSupportedException)
        {
        }

        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = AppContext.BaseDirectory;
        }

        return Path.Combine(root, "IA27Terminal", "config.json");
    }

    private static bool IsWritable(string directory)
    {
        var probe = Path.Combine(directory, $".ia27-write-test-{Environment.ProcessId}.tmp");
        try
        {
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
