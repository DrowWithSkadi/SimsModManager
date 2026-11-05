using SharpCompress.Archives;
using SharpCompress.Common;

namespace SimsModManager.Core.Archives;

/// <summary>Распаковка ZIP / RAR / 7z через библиотеку SharpCompress.</summary>
public sealed class SharpCompressExtractor : IArchiveExtractor
{
    public void ExtractAll(string archivePath, string destination)
    {
        Directory.CreateDirectory(destination);
        try
        {
            using var archive = ArchiveFactory.Open(archivePath);
            // ExtractAllEntries читает архив последовательно — так быстро работают и «solid» RAR/7z.
            using var reader = archive.ExtractAllEntries();
            while (reader.MoveToNextEntry())
            {
                var entry = reader.Entry;
                if (entry.IsDirectory || string.IsNullOrEmpty(entry.Key)) continue;

                var target = SafePath.Resolve(destination, entry.Key);
                if (target is null) continue; // подозрительный путь — пропускаем

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var output = File.Create(target);
                reader.WriteEntryTo(output);
            }
        }
        catch (CryptographicException ex)
        {
            throw new ArchiveExtractionException("Архив защищён паролем.", ex);
        }
        catch (InvalidFormatException ex)
        {
            throw new ArchiveExtractionException("Не удалось открыть архив: он повреждён или имеет неизвестный формат.", ex);
        }
        catch (ArchiveException ex)
        {
            throw new ArchiveExtractionException("Ошибка при распаковке архива: " + ex.Message, ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new ArchiveExtractionException("Не удалось открыть архив: " + ex.Message, ex);
        }
    }
}
