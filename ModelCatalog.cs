namespace IaTerminal;

public sealed record ModelDescriptor(
    string Id,
    string Name,
    string Path,
    string RelativePath,
    long SizeBytes,
    DateTime LastWriteTimeUtc)
{
    public DateTime LastWriteTimeLocal => LastWriteTimeUtc.ToLocalTime();
}

public sealed class ModelCatalog
{
    private readonly string root;

    public ModelCatalog(string root)
    {
        this.root = Path.GetFullPath(root);
    }

    public string Root => root;

    public IReadOnlyList<ModelDescriptor> List()
    {
        if (!Directory.Exists(root))
        {
            return Array.Empty<ModelDescriptor>();
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.System
        };

        try
        {
            return Directory.EnumerateFiles(root, "*.gguf", options)
                .Where(path => string.Equals(Path.GetExtension(path), ".gguf", StringComparison.OrdinalIgnoreCase))
                .Select(CreateDescriptor)
                .OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(model => model.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (IOException)
        {
            return Array.Empty<ModelDescriptor>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<ModelDescriptor>();
        }
    }

    public bool TryResolve(string? selector, out ModelDescriptor? model)
    {
        model = null;
        var models = List();
        if (models.Count == 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(selector))
        {
            model = models[0];
            return true;
        }

        var value = selector.Trim().Trim('"');
        if (int.TryParse(value, out var index) && index > 0 && index <= models.Count)
        {
            model = models[index - 1];
            return true;
        }

        var directPath = Path.IsPathRooted(value)
            ? value
            : Path.Combine(root, value);
        if (File.Exists(directPath) && string.Equals(Path.GetExtension(directPath), ".gguf", StringComparison.OrdinalIgnoreCase))
        {
            model = CreateDescriptor(Path.GetFullPath(directPath));
            return true;
        }

        var matches = models.Where(candidate =>
            string.Equals(candidate.Name, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.Id, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.Path, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.RelativePath, value, StringComparison.OrdinalIgnoreCase)).ToArray();

        if (matches.Length == 0)
        {
            return false;
        }

        model = matches[0];
        return true;
    }

    public ModelDescriptor Resolve(string? selector)
    {
        if (TryResolve(selector, out var model) && model is not null)
        {
            return model;
        }

        if (string.IsNullOrWhiteSpace(selector))
        {
            throw new TerminalException($"No se encontraron modelos GGUF en: {root}");
        }

        throw new TerminalException($"No se encontró el modelo '{selector}'. Usa 'listar' para ver los disponibles.");
    }

    private ModelDescriptor CreateDescriptor(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var file = new FileInfo(fullPath);
        var name = file.Name;
        var id = Path.GetFileNameWithoutExtension(name);
        string relative;
        try
        {
            relative = Path.GetRelativePath(root, fullPath);
        }
        catch
        {
            relative = fullPath;
        }

        return new ModelDescriptor(id, name, fullPath, relative, file.Length, file.LastWriteTimeUtc);
    }
}
