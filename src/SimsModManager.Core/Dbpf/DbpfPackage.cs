using System.Buffers.Binary;
using System.IO.Compression;

namespace SimsModManager.Core.Dbpf;

/// <summary>Ключ ресурса: тип, группа, номер (в моддинге «TGI»).</summary>
public readonly record struct ResourceKey(uint Type, uint Group, ulong Instance)
{
    public override string ToString() => $"{Type:X8}:{Group:X8}:{Instance:X16}";
}

public sealed record ResourceEntry(
    ResourceKey Key,
    uint Offset,
    uint StoredSize,
    uint MemorySize,
    ushort Compression,
    ushort Committed)
{
    public bool IsDeleted => Compression == DbpfPackage.CompressionDeleted;
}

/// <summary>
/// Файл .package (формат DBPF 2.1), в котором игра хранит моды: одежду, объекты, картинки, настройки.
/// Класс читает оглавление и отдельные ресурсы, не загружая весь файл в память.
/// </summary>
public sealed class DbpfPackage : IDisposable
{
    public const ushort CompressionNone = 0x0000;
    public const ushort CompressionZlib = 0x5A42;
    public const ushort CompressionRefPack = 0xFFFF;
    public const ushort CompressionStreamable = 0xFFFE;
    public const ushort CompressionDeleted = 0xFFE0;

    private const int HeaderSize = 96;
    private readonly Stream _stream;

    public IReadOnlyList<ResourceEntry> Entries { get; }

    private DbpfPackage(Stream stream, List<ResourceEntry> entries)
    {
        _stream = stream;
        Entries = entries;
    }

    public static DbpfPackage Open(string path) =>
        Open(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024));

    public static DbpfPackage Open(Stream stream)
    {
        try
        {
            return new DbpfPackage(stream, ReadIndex(stream));
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static List<ResourceEntry> ReadIndex(Stream s)
    {
        Span<byte> header = stackalloc byte[HeaderSize];
        if (s.Length < HeaderSize) throw new InvalidDataException("Файл слишком маленький для .package.");
        s.Position = 0;
        s.ReadExactly(header);

        if (header[0] != 'D' || header[1] != 'B' || header[2] != 'P' || header[3] != 'F')
            throw new InvalidDataException("Это не файл .package (нет подписи DBPF).");

        var major = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
        if (major != 2) throw new InvalidDataException($"Неподдерживаемая версия .package: {major}.");

        var count = BinaryPrimitives.ReadInt32LittleEndian(header[36..]);
        var indexSize = BinaryPrimitives.ReadInt32LittleEndian(header[44..]);
        long indexPos = BinaryPrimitives.ReadUInt32LittleEndian(header[64..]);
        if (indexPos == 0) indexPos = BinaryPrimitives.ReadUInt32LittleEndian(header[40..]);

        var entries = new List<ResourceEntry>(Math.Max(count, 0));
        if (count <= 0) return entries;
        if (indexPos <= 0 || indexPos + indexSize > s.Length || indexSize < 4)
            throw new InvalidDataException("Оглавление .package повреждено.");

        var index = new byte[indexSize];
        s.Position = indexPos;
        s.ReadExactly(index);
        var r = new SpanReader(index);

        var flags = r.U32();
        uint constType = 0, constGroup = 0, constInstHi = 0;
        if ((flags & 1) != 0) constType = r.U32();
        if ((flags & 2) != 0) constGroup = r.U32();
        if ((flags & 4) != 0) constInstHi = r.U32();

        for (var i = 0; i < count; i++)
        {
            var type = (flags & 1) != 0 ? constType : r.U32();
            var group = (flags & 2) != 0 ? constGroup : r.U32();
            var instHi = (flags & 4) != 0 ? constInstHi : r.U32();
            var instLo = r.U32();
            var offset = r.U32();
            var fileSize = r.U32();
            var memSize = r.U32();
            ushort compression = 0, committed = 1;
            if ((fileSize & 0x80000000) != 0)
            {
                compression = r.U16();
                committed = r.U16();
            }

            entries.Add(new ResourceEntry(
                new ResourceKey(type, group, ((ulong)instHi << 32) | instLo),
                offset, fileSize & 0x7FFFFFFF, memSize, compression, committed));
        }
        return entries;
    }

    /// <summary>Байты ресурса в том виде, как они лежат в файле (возможно, сжатые).</summary>
    public byte[] ReadRaw(ResourceEntry entry)
    {
        if (entry.Offset + (long)entry.StoredSize > _stream.Length)
            throw new InvalidDataException("Ресурс выходит за границы файла.");
        var data = new byte[entry.StoredSize];
        _stream.Position = entry.Offset;
        _stream.ReadExactly(data);
        return data;
    }

    /// <summary>Распакованные байты ресурса.</summary>
    public byte[] Read(ResourceEntry entry)
    {
        var raw = ReadRaw(entry);
        return entry.Compression switch
        {
            CompressionNone => raw,
            CompressionZlib => InflateZlib(raw, entry.MemorySize),
            CompressionRefPack => RefPack.Decompress(raw),
            CompressionDeleted => [],
            _ => throw new InvalidDataException($"Неизвестное сжатие ресурса: 0x{entry.Compression:X4}."),
        };
    }

    private static byte[] InflateZlib(byte[] raw, uint memSize)
    {
        using var input = new MemoryStream(raw);
        using var z = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream((int)Math.Min(memSize, 64 * 1024 * 1024));
        z.CopyTo(output);
        return output.ToArray();
    }

    public void Dispose() => _stream.Dispose();

    /// <summary>Мини-читатель little-endian чисел из массива байт.</summary>
    private ref struct SpanReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _pos;

        public uint U32()
        {
            if (_pos + 4 > _data.Length) throw new InvalidDataException("Оглавление .package обрывается.");
            var v = BinaryPrimitives.ReadUInt32LittleEndian(_data[_pos..]);
            _pos += 4;
            return v;
        }

        public ushort U16()
        {
            if (_pos + 2 > _data.Length) throw new InvalidDataException("Оглавление .package обрывается.");
            var v = BinaryPrimitives.ReadUInt16LittleEndian(_data[_pos..]);
            _pos += 2;
            return v;
        }
    }
}

/// <summary>Запись .package. Нужна для изменения настроек одежды и для тестов.</summary>
public static class DbpfWriter
{
    public sealed record Resource(ResourceKey Key, byte[] Data, bool Compress = true);

    public static void Write(Stream output, IEnumerable<Resource> resources)
    {
        var list = resources.ToList();
        var w = new BinaryWriter(output);

        // Заголовок пока пустой — заполним в конце, когда узнаем, где оглавление.
        w.Write(new byte[96]);

        var placed = new List<(ResourceKey Key, uint Offset, uint Stored, uint Mem, ushort Comp)>();
        foreach (var res in list)
        {
            var offset = (uint)output.Position;
            byte[] stored;
            ushort comp;
            if (res.Compress && res.Data.Length > 0)
            {
                using var ms = new MemoryStream();
                using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                    z.Write(res.Data);
                stored = ms.ToArray();
                comp = DbpfPackage.CompressionZlib;
            }
            else
            {
                stored = res.Data;
                comp = DbpfPackage.CompressionNone;
            }
            w.Write(stored);
            placed.Add((res.Key, offset, (uint)stored.Length, (uint)res.Data.Length, comp));
        }

        var indexPos = (uint)output.Position;
        w.Write(0u); // флаги оглавления: все поля у каждой записи
        foreach (var p in placed)
        {
            w.Write(p.Key.Type);
            w.Write(p.Key.Group);
            w.Write((uint)(p.Key.Instance >> 32));
            w.Write((uint)(p.Key.Instance & 0xFFFFFFFF));
            w.Write(p.Offset);
            w.Write(p.Stored | 0x80000000);
            w.Write(p.Mem);
            w.Write(p.Comp);
            w.Write((ushort)1);
        }
        var indexSize = (uint)(output.Position - indexPos);
        var end = output.Position;

        output.Position = 0;
        w.Write("DBPF"u8.ToArray());
        w.Write(2); // major
        w.Write(1); // minor
        output.Position = 36;
        w.Write(placed.Count);
        w.Write(0);  // старое поле позиции оглавления
        w.Write(indexSize);
        output.Position = 60;
        w.Write(3);  // версия оглавления
        w.Write(indexPos);
        output.Position = end;
        w.Flush();
    }
}
