using SimsModManager.Core;

namespace SimsModManager.App.ViewModels;

/// <summary>Форматирование чисел для интерфейса.</summary>
public static class Text
{
    public static string Size(long bytes)
    {
        string[] units = [L.T("Б"), L.T("КБ"), L.T("МБ"), L.T("ГБ")];
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
