using System.Globalization;
using System.Text.Json;

namespace SimsModManager.Core;

/// <summary>
/// Перевод интерфейса. Исходный язык — русский: русская строка в коде и есть ключ,
/// а английский перевод лежит в Localization/en.json («русский текст»: «английский»).
/// Если перевода нет, показывается русский текст — программа не ломается.
/// </summary>
public static class L
{
    public const string Russian = "ru";
    public const string English = "en";

    private static readonly Dictionary<string, string> En = LoadTable("en.json");
    private static string _lang = Russian;

    /// <summary>Текущий язык: «ru» или «en».</summary>
    public static string Lang => _lang;

    public static bool IsEnglish => _lang == English;

    /// <summary>Срабатывает после смены языка (из любого потока).</summary>
    public static event Action? Changed;

    /// <summary>
    /// Выбор из настроек: «ru», «en» или пусто — тогда по языку Windows
    /// (русский для языков СНГ, иначе английский).
    /// </summary>
    public static string Resolve(string? setting) => setting switch
    {
        Russian => Russian,
        English => English,
        _ => Detect(CultureInfo.CurrentUICulture),
    };

    public static string Detect(CultureInfo culture)
    {
        // ro-MD — Молдова; остальные — языки, где русский обычно понимают лучше английского
        var name = culture.Name;
        if (name.StartsWith("ro-MD", StringComparison.OrdinalIgnoreCase)) return Russian;
        return culture.TwoLetterISOLanguageName switch
        {
            "ru" or "uk" or "be" or "kk" or "ky" or "uz" or "hy" or "az" or "ka" or "tg" or "tk" => Russian,
            _ => English,
        };
    }

    public static void SetLanguage(string lang)
    {
        lang = lang == English ? English : Russian;
        if (lang == _lang) return;
        _lang = lang;
        Changed?.Invoke();
    }

    /// <summary>Культура для дат и чисел в текущем языке.</summary>
    public static CultureInfo Culture => IsEnglish ? CultureInfo.GetCultureInfo("en-US") : CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>Перевод строки.</summary>
    public static string T(string ru) =>
        _lang == English && En.TryGetValue(ru, out var en) ? en : ru;

    /// <summary>Перевод строки с подстановкой: L.F("Удалено {0} из {1}", a, b).</summary>
    public static string F(string ru, params object?[] args) =>
        string.Format(Culture, T(ru), args);

    /// <summary>
    /// Число со словом в нужной форме: L.P(5, "мод|мода|модов") → «5 модов», в английском «5 mods».
    /// Перевод формы задаётся строкой «mod|mods».
    /// </summary>
    public static string P(long n, string forms) => $"{n.ToString("N0", Culture)} {Word(n, forms)}";

    /// <summary>Только слово без числа: L.Word(5, "мод|мода|модов") → «модов».</summary>
    public static string Word(long n, string forms)
    {
        var parts = T(forms).Split('|');
        if (_lang == English || parts.Length < 3)
            return parts.Length == 1 || n == 1 ? parts[0] : parts[1];
        var mod100 = Math.Abs(n) % 100;
        var mod10 = Math.Abs(n) % 10;
        return mod100 is >= 11 and <= 14 ? parts[2]
            : mod10 == 1 ? parts[0]
            : mod10 is >= 2 and <= 4 ? parts[1]
            : parts[2];
    }

    /// <summary>Все русские строки, у которых есть английский перевод (для тестов).</summary>
    public static IReadOnlyDictionary<string, string> EnglishTable => En;

    private static Dictionary<string, string> LoadTable(string name)
    {
        try
        {
            var asm = typeof(L).Assembly;
            var resource = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("Localization." + name, StringComparison.Ordinal));
            if (resource is null) return new(StringComparer.Ordinal);
            using var stream = asm.GetManifestResourceStream(resource)!;
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new(StringComparer.Ordinal);
        }
    }
}
