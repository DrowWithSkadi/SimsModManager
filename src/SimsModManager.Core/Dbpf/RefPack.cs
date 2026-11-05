namespace SimsModManager.Core.Dbpf;

/// <summary>
/// Распаковка «внутреннего» сжатия EA (RefPack / QFS), которым игра иногда жмёт ресурсы в .package.
/// </summary>
public static class RefPack
{
    public static byte[] Decompress(ReadOnlySpan<byte> data)
    {
        if (data.Length < 5 || data[1] != 0xFB)
            throw new InvalidDataException("Неизвестный формат сжатия RefPack.");

        var flags = data[0];
        var pos = 2;
        var sizeBytes = (flags & 0x80) != 0 ? 4 : 3;
        if ((flags & 0x01) != 0) pos += sizeBytes; // размер в сжатом виде нам не нужен

        var size = 0;
        for (var i = 0; i < sizeBytes; i++) size = (size << 8) | data[pos++];

        var output = new byte[size];
        var outPos = 0;

        while (pos < data.Length)
        {
            int b0 = data[pos++];
            int plain, copy = 0, offset = 0;

            if (b0 < 0x80)
            {
                int b1 = data[pos++];
                plain = b0 & 0x03;
                copy = ((b0 & 0x1C) >> 2) + 3;
                offset = ((b0 & 0x60) << 3) + b1 + 1;
            }
            else if (b0 < 0xC0)
            {
                int b1 = data[pos++], b2 = data[pos++];
                plain = (b1 >> 6) & 0x03;
                copy = (b0 & 0x3F) + 4;
                offset = ((b1 & 0x3F) << 8) + b2 + 1;
            }
            else if (b0 < 0xE0)
            {
                int b1 = data[pos++], b2 = data[pos++], b3 = data[pos++];
                plain = b0 & 0x03;
                copy = ((b0 & 0x0C) << 6) + b3 + 5;
                offset = ((b0 & 0x10) << 12) + (b1 << 8) + b2 + 1;
            }
            else if (b0 < 0xFC)
            {
                plain = ((b0 & 0x1F) << 2) + 4;
            }
            else
            {
                plain = b0 & 0x03;
                Copy(data, ref pos, output, ref outPos, plain);
                break; // конец потока
            }

            Copy(data, ref pos, output, ref outPos, plain);

            if (copy > 0)
            {
                var from = outPos - offset;
                if (from < 0 || outPos + copy > output.Length)
                    throw new InvalidDataException("Повреждённые данные RefPack.");
                // Побайтно: области могут перекрываться — это нормально для этого формата.
                for (var i = 0; i < copy; i++) output[outPos++] = output[from + i];
            }
        }

        return output;
    }

    private static void Copy(ReadOnlySpan<byte> src, ref int pos, byte[] dst, ref int dstPos, int count)
    {
        if (count == 0) return;
        if (pos + count > src.Length || dstPos + count > dst.Length)
            throw new InvalidDataException("Повреждённые данные RefPack.");
        src.Slice(pos, count).CopyTo(dst.AsSpan(dstPos));
        pos += count;
        dstPos += count;
    }
}
