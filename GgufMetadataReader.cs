using System.Text;

namespace IaTerminal;

public sealed record GgufInfo(
    uint Version,
    ulong TensorCount,
    ulong MetadataCount,
    IReadOnlyDictionary<string, object?> Metadata);

public static class GgufMetadataReader
{
    private const uint GgufMagic = 0x46554747;

    public static GgufInfo? TryRead(string path)
    {
        try
        {
            return Read(path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static GgufInfo Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);

        var magic = reader.ReadUInt32();
        if (magic != GgufMagic)
        {
            throw new InvalidDataException("El archivo no tiene una cabecera GGUF válida.");
        }

        var version = reader.ReadUInt32();
        var tensorCount = reader.ReadUInt64();
        var metadataCount = reader.ReadUInt64();
        var metadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        for (ulong index = 0; index < metadataCount; index++)
        {
            var key = ReadString(reader, stream);
            var valueType = reader.ReadUInt32();
            if (ShouldStore(key))
            {
                metadata[key] = ReadValue(reader, stream, valueType, 0);
            }
            else
            {
                SkipValue(reader, stream, valueType, 0);
            }
        }

        for (ulong index = 0; index < tensorCount; index++)
        {
            ReadString(reader, stream);
            var dimensions = reader.ReadUInt32();
            for (uint dimension = 0; dimension < dimensions; dimension++)
            {
                reader.ReadUInt64();
            }

            reader.ReadUInt32();
            reader.ReadUInt64();
        }

        return new GgufInfo(version, tensorCount, metadataCount, metadata);
    }

    private static bool ShouldStore(string key)
    {
        return key.StartsWith("general.", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("tokenizer.chat_template", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("tokenizer.ggml.model", StringComparison.OrdinalIgnoreCase) ||
               key.EndsWith(".context_length", StringComparison.OrdinalIgnoreCase) ||
               key.EndsWith(".block_count", StringComparison.OrdinalIgnoreCase) ||
               key.EndsWith(".embedding_length", StringComparison.OrdinalIgnoreCase) ||
               key.EndsWith(".feed_forward_length", StringComparison.OrdinalIgnoreCase) ||
               key.EndsWith(".attention.head_count", StringComparison.OrdinalIgnoreCase) ||
               key.EndsWith(".attention.head_count_kv", StringComparison.OrdinalIgnoreCase) ||
               key.EndsWith(".rope.dimension_count", StringComparison.OrdinalIgnoreCase);
    }

    private static object? ReadValue(BinaryReader reader, FileStream stream, uint valueType, int depth)
    {
        if (depth > 8)
        {
            throw new InvalidDataException("Estructura GGUF demasiado anidada.");
        }

        return valueType switch
        {
            0 => reader.ReadByte(),
            1 => reader.ReadSByte(),
            2 => reader.ReadUInt16(),
            3 => reader.ReadInt16(),
            4 => reader.ReadUInt32(),
            5 => reader.ReadInt32(),
            6 => reader.ReadSingle(),
            7 => reader.ReadByte() != 0,
            8 => ReadString(reader, stream),
            9 => ReadArray(reader, stream, depth + 1),
            10 => reader.ReadUInt64(),
            11 => reader.ReadInt64(),
            12 => reader.ReadDouble(),
            _ => throw new InvalidDataException($"Tipo GGUF desconocido: {valueType}.")
        };
    }

    private static GgufArrayInfo ReadArray(BinaryReader reader, FileStream stream, int depth)
    {
        var elementType = reader.ReadUInt32();
        var count = ReadCount(reader, stream);
        for (ulong index = 0; index < count; index++)
        {
            SkipValue(reader, stream, elementType, depth);
        }

        return new GgufArrayInfo(elementType, count);
    }

    private static void SkipValue(BinaryReader reader, FileStream stream, uint valueType, int depth)
    {
        if (depth > 8)
        {
            throw new InvalidDataException("Estructura GGUF demasiado anidada.");
        }

        switch (valueType)
        {
            case 0:
            case 1:
            case 7:
                reader.ReadByte();
                break;
            case 2:
            case 3:
                reader.ReadUInt16();
                break;
            case 4:
            case 5:
                reader.ReadUInt32();
                break;
            case 6:
                reader.ReadSingle();
                break;
            case 8:
                SkipString(reader, stream);
                break;
            case 9:
                var elementType = reader.ReadUInt32();
                var count = ReadCount(reader, stream);
                for (ulong index = 0; index < count; index++)
                {
                    SkipValue(reader, stream, elementType, depth + 1);
                }
                break;
            case 10:
            case 11:
            case 12:
                reader.ReadUInt64();
                break;
            default:
                throw new InvalidDataException($"Tipo GGUF desconocido: {valueType}.");
        }
    }

    private static string ReadString(BinaryReader reader, FileStream stream)
    {
        var length = ReadCount(reader, stream);
        if (length > int.MaxValue || length > (ulong)(stream.Length - stream.Position))
        {
            throw new InvalidDataException("Longitud de texto GGUF inválida.");
        }

        var bytes = reader.ReadBytes((int)length);
        if ((ulong)bytes.Length != length)
        {
            throw new EndOfStreamException();
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static void SkipString(BinaryReader reader, FileStream stream)
    {
        var length = ReadCount(reader, stream);
        SkipBytes(reader, stream, length);
    }

    private static ulong ReadCount(BinaryReader reader, FileStream stream)
    {
        var count = reader.ReadUInt64();
        if (count > (ulong)(stream.Length - stream.Position))
        {
            throw new InvalidDataException("Longitud GGUF fuera de rango.");
        }

        return count;
    }

    private static void SkipBytes(BinaryReader reader, FileStream stream, ulong count)
    {
        while (count > 0)
        {
            var chunk = (int)Math.Min(count, 1024 * 1024);
            var skipped = reader.ReadBytes(chunk);
            if (skipped.Length != chunk)
            {
                throw new EndOfStreamException();
            }

            count -= (uint)chunk;
        }
    }
}

public sealed record GgufArrayInfo(uint ElementType, ulong Count)
{
    public override string ToString()
    {
        return $"array[{Count}]";
    }
}
