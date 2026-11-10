namespace SimsModManager.Core;

/// <summary>Состояние дополнения в папке игры.</summary>
public enum PackState
{
    /// <summary>Папки нет.</summary>
    Missing,
    /// <summary>Папка есть, но полных файлов нет (только обновления) — вещи будут невидимыми или «квадратами».</summary>
    Partial,
    Installed,
}

/// <param name="Code">Код папки в игре: EP05, GP01, SP03…</param>
/// <param name="Id">Номер дополнения внутри файлов (поле PackID у вещей).</param>
public sealed record PackInfo(string Code, int Id, string Name)
{
    public bool IsExpansion => Code.StartsWith("EP", StringComparison.Ordinal);
    public bool IsGamePack => Code.StartsWith("GP", StringComparison.Ordinal);
}

/// <summary>Дополнения The Sims 4: какие есть, как называются, какие установлены.</summary>
public static class GamePacks
{
    // Номера — из перечисления Pack самой игры (видно в её отчётах об ошибках) и справочника моддеров.
    public static readonly IReadOnlyList<PackInfo> All =
    [
        new("EP01", 3, "На работу"),
        new("EP02", 9, "Веселимся вместе!"),
        new("EP03", 24, "Жизнь в городе"),
        new("EP04", 25, "Кошки и собаки"),
        new("EP05", 26, "Времена года"),
        new("EP06", 27, "Путь к славе"),
        new("EP07", 28, "Жизнь на острове"),
        new("EP08", 29, "В университете"),
        new("EP09", 30, "Экологичная жизнь"),
        new("EP10", 31, "Снежные просторы"),
        new("EP11", 52, "Загородная жизнь"),
        new("EP12", 53, "Старшая школа"),
        new("EP13", 54, "Growing Together"),
        new("EP14", 55, "Конное ранчо"),
        new("EP15", 56, "For Rent"),
        new("EP16", 57, "Lovestruck"),
        new("EP17", 58, "Жизнь и смерть"),
        new("EP18", 59, "Businesses & Hobbies"),
        new("EP19", 60, "Enchanted by Nature"),
        new("EP20", 61, "Расширение EP20"),

        new("GP01", 2, "В поход"),
        new("GP02", 6, "День спа"),
        new("GP03", 10, "Dine Out"),
        new("GP04", 17, "Вампиры"),
        new("GP05", 18, "Родители"),
        new("GP06", 19, "Приключения в джунглях"),
        new("GP07", 20, "StrangerVille"),
        new("GP08", 21, "Мир магии"),
        new("GP09", 22, "Star Wars: Journey to Batuu"),
        new("GP10", 23, "Dream Home Decorator"),
        new("GP11", 62, "My Wedding Stories"),

        new("FP01", 5, "Holiday Celebration"),

        new("SP01", 1, "Luxury Party Stuff"), new("SP02", 4, "Perfect Patio Stuff"), new("SP03", 7, "Cool Kitchen Stuff"),
        new("SP04", 8, "Spooky Stuff"), new("SP05", 11, "Movie Hangout Stuff"), new("SP06", 12, "Romantic Garden Stuff"),
        new("SP07", 13, "Kids Room Stuff"), new("SP08", 14, "Backyard Stuff"), new("SP09", 15, "Vintage Glamour Stuff"),
        new("SP10", 16, "Bowling Night Stuff"), new("SP11", 32, "Fitness Stuff"), new("SP12", 33, "Toddler Stuff"),
        new("SP13", 34, "Laundry Day Stuff"), new("SP14", 35, "My First Pet Stuff"), new("SP15", 36, "Moschino Stuff"),
        new("SP16", 37, "Tiny Living Stuff"), new("SP17", 38, "Nifty Knitting Stuff"), new("SP18", 39, "Paranormal Stuff"),
        new("SP20", 41, "Throwback Fit"), new("SP21", 42, "Country Kitchen"), new("SP22", 43, "Bust The Dust"),
        new("SP23", 44, "Courtyard Oasis"), new("SP24", 45, "Fashion Street"), new("SP25", 46, "Industrial Loft"),
        new("SP26", 47, "Incheon Arrivals"), new("SP28", 49, "Modern Menswear"), new("SP29", 50, "Blooming Rooms"),
        new("SP30", 51, "Carnaval Streetwear"), new("SP31", 72, "Decor to the Max"), new("SP32", 73, "Moonlight Chic"),
        new("SP33", 74, "Little Campers"), new("SP34", 75, "First Fits"), new("SP35", 76, "Desert Luxe"),
        new("SP36", 77, "Pastel Pop"), new("SP37", 78, "Everyday Clutter"), new("SP38", 79, "Simtimates Collection"),
        new("SP39", 80, "Bathroom Clutter"), new("SP40", 81, "Greenhouse Haven"), new("SP41", 82, "Basement Treasures"),
        new("SP42", 83, "Grunge Revival"), new("SP43", 84, "Book Nook"), new("SP44", 85, "Poolside Splash"),
        new("SP45", 86, "Modern Luxe"), new("SP46", 87, "Home Chef Hustle Stuff"), new("SP47", 88, "Castle Estate"),
        new("SP48", 89, "Goth Galore"), new("SP49", 90, "Crystal Creations Stuff"), new("SP50", 91, "Urban Homage"),
        new("SP51", 92, "Party Essentials"), new("SP52", 93, "Riviera Retreat"), new("SP53", 94, "Cozy Bistro"),
        new("SP54", 95, "Artist Studio"), new("SP55", 96, "Storybook Nursery"), new("SP56", 97, "Sweet Slumber Party"),
        new("SP57", 98, "Cozy Kitsch"), new("SP58", 99, "Comfy Gamer"), new("SP59", 100, "Secret Sanctuary"),
        new("SP60", 101, "Casanova Cave"), new("SP61", 102, "Refined Living Room"), new("SP62", 103, "Business Chic"),
        new("SP63", 104, "Sleek Bathroom"), new("SP64", 105, "Sweet Allure"), new("SP65", 106, "Restoration Workshop"),
        new("SP66", 107, "Golden Years"), new("SP67", 108, "Kitchen Clutter"), new("SP68", 109, "SpongeBob's House"),
        new("SP69", 110, "Autumn Apparel"), new("SP70", 111, "SpongeBob Kid's Rooms"), new("SP71", 112, "Grange Mudroom"),
        new("SP72", 113, "Essential Glam"), new("SP73", 114, "Modern Retreat"), new("SP74", 115, "Garden to Table"),
        new("SP75", 116, "Wonderland Playroom"), new("SP76", 117, "Silver Screen Style"), new("SP77", 118, "Tea Time Solarium"),
        new("SP78", 119, "Masquerade Ball Fashion Kit"), new("SP79", 120, "Masquerade Ballroom Kit"), new("SP80", 121, "Music Den"),
        new("SP81", 122, "Prairie Dreams"), new("SP82", 123, "Yard Charm"), new("SP83", 124, "Mean Girls Capsule"),
        new("SP84", 125, "Clueless Capsule"), new("SP87", 128, "Coach RePurposed Capsule"),
    ];

    public const string Seasons = "EP05";

    private static readonly Dictionary<int, PackInfo> ById = All.ToDictionary(p => p.Id);
    private static readonly Dictionary<string, PackInfo> ByCode = All.ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);

    public static PackInfo? FromId(int id) => ById.GetValueOrDefault(id);
    public static PackInfo? FromCode(string code) => ByCode.GetValueOrDefault(code);

    /// <summary>Название по коду (EP05 → «Времена года»); неизвестный код возвращается как есть.</summary>
    public static string NameOf(string code) => FromCode(code)?.Name ?? code;

    /// <summary>
    /// Какие дополнения лежат в папке установки игры. Дополнение считается установленным,
    /// только если в его папке есть полные файлы (ClientFullBuild / SimulationFullBuild) —
    /// одних обновлений из Delta мало: вещи будут невидимыми или «квадратами».
    /// </summary>
    public static Dictionary<string, PackState> Scan(string installRoot)
    {
        var result = new Dictionary<string, PackState>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(installRoot)) return result;
        foreach (var dir in Directory.EnumerateDirectories(installRoot))
        {
            var code = Path.GetFileName(dir).ToUpperInvariant();
            if (code.Length != 4 || !(code.StartsWith("EP") || code.StartsWith("GP") || code.StartsWith("SP") || code.StartsWith("FP"))
                || !char.IsDigit(code[2]) || !char.IsDigit(code[3])) continue;
            bool full;
            try
            {
                full = Directory.EnumerateFiles(dir, "*FullBuild*.package").Any();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { full = false; }
            result[code] = full ? PackState.Installed : PackState.Partial;
        }
        return result;
    }
}
