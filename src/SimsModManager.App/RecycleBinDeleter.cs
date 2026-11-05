using Microsoft.VisualBasic.FileIO;
using SimsModManager.Core;

namespace SimsModManager.App;

/// <summary>Удаляет моды в корзину Windows, чтобы случайно удалённое можно было вернуть.</summary>
public sealed class RecycleBinDeleter : IFileDeleter
{
    public void DeleteFile(string path) =>
        FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);

    public void DeleteDirectory(string path) =>
        FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
}
