namespace SimsModManager.App.ViewModels;

/// <summary>Форматирование чисел для интерфейса.</summary>
public static class Text
{
    /// <summary>1 файл, 2 файла, 5 файлов.</summary>
    public static string Plural(int n, string one, string few, string many)
    {
        var mod100 = n % 100;
        var mod10 = n % 10;
        var word = mod100 is >= 11 and <= 14 ? many
            : mod10 == 1 ? one
            : mod10 is >= 2 and <= 4 ? few
            : many;
        return $"{n} {word}";
    }

    public static string Size(long bytes)
    {
        string[] units = ["Б", "КБ", "МБ", "ГБ"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} {units[0]}" : $"{value:0.#} {units[unit]}";
    }
}
