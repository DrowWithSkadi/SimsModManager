using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using SimsModManager.Core;
using SimsModManager.Core.Content;

namespace SimsModManager.Tests;

/// <summary>Тесты, которые меняют язык, идут отдельно от остальных — язык общий на всю программу.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LanguageCollection
{
    public const string Name = "Язык интерфейса";
}

[Collection(LanguageCollection.Name)]
public sealed class LocalizationTests
{
    // ---------- Проверка исходников: у каждой строки интерфейса есть английский перевод ----------

    private static readonly Regex CyrillicPattern = new("[А-Яа-яЁё]");
    private const string Literal = @"(@?)""((?:[^""\\]|\\.|"""")*)""";
    private static readonly Regex TfCall = new(@"\bL\.(?:T|F)\(\s*" + Literal);
    private static readonly Regex PluralCall = new(@"\bL\.(?:P|Word)\(");
    private static readonly Regex PluralForms = new(@"^[^;]*?,\s*" + Literal + @"\s*\)");
    private static readonly Regex XamlTr = new(@"\{l:Tr '((?:[^'\\]|\\.)*)'\}");
    private static readonly Regex Comment = new(@"//[^\n]*");
    private static readonly Regex XamlComment = new(@"<!--.*?-->", RegexOptions.Singleline);
    private static readonly Regex Placeholder = new(@"\{(\d+)");

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SimsModManager.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src");
    }

    private static IEnumerable<string> Files(string pattern) =>
        Directory.EnumerateFiles(SourceRoot(), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static string Unescape(string prefix, string body) =>
        prefix == "@" ? body.Replace("\"\"", "\"") : Regex.Unescape(body);

    /// <summary>Все русские строки, которые программа переводит: из L.T/L.F/L.P в коде и {l:Tr} в разметке.</summary>
    private static Dictionary<string, string> UsedKeys()
    {
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Files("*.cs"))
        {
            var code = Comment.Replace(File.ReadAllText(file), "");
            foreach (Match m in TfCall.Matches(code))
                keys.TryAdd(Unescape(m.Groups[1].Value, m.Groups[2].Value), Path.GetFileName(file));
            foreach (Match m in PluralCall.Matches(code))
            {
                var rest = code.Substring(m.Index + m.Length, Math.Min(300, code.Length - m.Index - m.Length));
                var forms = PluralForms.Match(rest);
                if (forms.Success && forms.Groups[2].Value.Contains('|'))
                    keys.TryAdd(Unescape(forms.Groups[1].Value, forms.Groups[2].Value), Path.GetFileName(file));
            }
        }
        foreach (var file in Files("*.xaml"))
            foreach (Match m in XamlTr.Matches(File.ReadAllText(file)))
                keys.TryAdd(WebUtility.HtmlDecode(Regex.Replace(m.Groups[1].Value, @"\\(.)", "$1")), Path.GetFileName(file));
        return keys;
    }

    /// <summary>Строки, которые переводятся через переменную: названия дополнений, категорий одежды, сезонов.</summary>
    private static IEnumerable<string> TableKeys() =>
        GamePacks.All.Select(p => p.RuName)
            .Concat(OutfitTags.Known.Select(k => k.Name))
            .Concat(OutfitTags.Weather.Select(k => k.Name))
            .Concat(SeasonTags.Known.Select(k => k.Name))
            .Where(s => CyrillicPattern.IsMatch(s));

    [Fact]
    public void EveryInterfaceStringHasEnglishTranslation()
    {
        var missing = UsedKeys().Where(k => !L.EnglishTable.ContainsKey(k.Key))
            .Select(k => $"{k.Value}: {k.Key}")
            .Concat(TableKeys().Where(k => !L.EnglishTable.ContainsKey(k)).Select(k => "таблица: " + k))
            .ToList();
        Assert.True(missing.Count == 0, "Нет английского перевода (Core/Localization/en.json):\n" + string.Join("\n", missing));
    }

    [Fact]
    public void TranslationTableHasNoLeftovers()
    {
        var used = UsedKeys().Keys.Concat(TableKeys()).ToHashSet(StringComparer.Ordinal);
        var unused = L.EnglishTable.Keys.Where(k => !used.Contains(k)).ToList();
        Assert.True(unused.Count == 0, "Переводы, которые больше нигде не используются — удалите их из en.json:\n" + string.Join("\n", unused));
    }

    [Fact]
    public void TranslationsKeepPlaceholdersAndPluralForms()
    {
        var problems = new List<string>();
        foreach (var (ru, en) in L.EnglishTable)
        {
            static string Holes(string s) => string.Join(",", Placeholder.Matches(s).Select(m => m.Groups[1].Value).Distinct().Order());
            if (Holes(ru) != Holes(en)) problems.Add($"подстановки: {ru} → {en}");
            if (ru.Contains('|') && !ru.Contains('*'))
            {
                if (ru.Split('|').Length != 3) problems.Add($"нужно три формы: {ru}");
                if (en.Split('|').Length != 2) problems.Add($"нужно две формы: {en}");
            }
            if (string.IsNullOrWhiteSpace(en)) problems.Add($"пустой перевод: {ru}");
            if (CyrillicPattern.IsMatch(en)) problems.Add($"в переводе русские буквы: {en}");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>Новая надпись по-русски без L.T / {l:Tr} не попадёт в английскую версию — ловим это сразу.</summary>
    [Fact]
    public void NoUntranslatedRussianTextInSources()
    {
        // Здесь русские строки — не текст интерфейса: имена папок на диске, записи в журнал,
        // таблицы, которые переводятся при показе (L.T(name)).
        string[] allowedFiles = ["SimsPaths.cs", "GameLocator.cs", "GamePacks.cs", "CasSettings.cs"];
        var anyLiteral = new Regex(@"(\$?@?)""((?:[^""\\\n]|\\.)*)""");
        var problems = new List<string>();
        foreach (var file in Files("*.cs").Where(f => !allowedFiles.Contains(Path.GetFileName(f))))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = Comment.Replace(lines[i], "");
                if (line.TrimStart().StartsWith("///") || line.Contains("Log(")) continue;
                foreach (Match m in anyLiteral.Matches(line))
                {
                    if (!CyrillicPattern.IsMatch(m.Groups[2].Value)) continue;
                    var before = line[..m.Index];
                    if (Regex.IsMatch(before, @"L\.(T|F)\(\s*$")) continue;
                    if (Regex.IsMatch(before, @"L\.(P|Word)\(.*,\s*$")) continue;
                    problems.Add($"{Path.GetFileName(file)}:{i + 1}: {m.Value}");
                }
            }
        }
        var attr = new Regex(@"([\w:.]+)=""([^""{][^""]*)""");
        foreach (var file in Files("*.xaml"))
        {
            var xaml = XamlComment.Replace(File.ReadAllText(file), "");
            foreach (Match m in attr.Matches(xaml))
                if (CyrillicPattern.IsMatch(m.Groups[2].Value) && m.Groups[2].Value != "Русский")
                    problems.Add($"{Path.GetFileName(file)}: {m.Value}");
        }
        Assert.True(problems.Count == 0,
            "Русский текст без перевода. Оберните в L.T(\"…\") / {l:Tr '…'} и добавьте перевод в en.json:\n" + string.Join("\n", problems));
    }

    // ---------- Поведение ----------

    [Fact]
    public void RussianPluralForms()
    {
        L.SetLanguage(L.Russian);
        Assert.Equal("1 мод", L.P(1, "мод|мода|модов"));
        Assert.Equal("3 мода", L.P(3, "мод|мода|модов"));
        Assert.Equal("5 модов", L.P(5, "мод|мода|модов"));
        Assert.Equal("11 модов", L.P(11, "мод|мода|модов"));
        Assert.Equal("21 мод", L.P(21, "мод|мода|модов"));
        Assert.Equal("модов", L.Word(12, "мод|мода|модов"));
    }

    [Fact]
    public void EnglishTextAndPlurals()
    {
        try
        {
            L.SetLanguage(L.English);
            Assert.Equal("1 mod", L.P(1, "мод|мода|модов"));
            Assert.Equal("3 mods", L.P(3, "мод|мода|модов"));
            Assert.Equal("Save", L.T("Сохранить"));
            Assert.Equal("Get to Work", GamePacks.NameOf("EP01"));
            // Нет перевода — показываем русский, а не пустоту
            Assert.Equal("Такой строки в таблице нет", L.T("Такой строки в таблице нет"));
        }
        finally
        {
            L.SetLanguage(L.Russian);
        }
        Assert.Equal("На работу", GamePacks.NameOf("EP01"));
    }

    [Fact]
    public void LanguageChangeRaisesEvent()
    {
        var raised = 0;
        void OnChanged() => raised++;
        L.Changed += OnChanged;
        try
        {
            L.SetLanguage(L.English);
            L.SetLanguage(L.English); // тот же язык — без события
            L.SetLanguage(L.Russian);
        }
        finally
        {
            L.Changed -= OnChanged;
            L.SetLanguage(L.Russian);
        }
        Assert.Equal(2, raised);
    }

    [Fact]
    public void DisabledFoldersFollowLanguageOnlyWhenNew()
    {
        try
        {
            L.SetLanguage(L.English);
            using (var t = new TestFolder())
            {
                var paths = new SimsPaths(t.Sims);
                Assert.EndsWith("Mods (disabled)", paths.DisabledMods);
                paths.EnsureCreated();
                L.SetLanguage(L.Russian);
                // папка уже есть — смена языка её не меняет
                Assert.EndsWith("Mods (disabled)", paths.DisabledMods);
            }
            using (var t = new TestFolder())
            {
                Directory.CreateDirectory(Path.Combine(t.Sims, "Mods (выключенные)"));
                L.SetLanguage(L.English);
                Assert.EndsWith("Mods (выключенные)", new SimsPaths(t.Sims).DisabledMods);
                Assert.EndsWith("Tray (disabled)", new SimsPaths(t.Sims).DisabledTray);
            }
        }
        finally
        {
            L.SetLanguage(L.Russian);
        }
    }

    [Theory]
    [InlineData("ru-RU", "ru")]
    [InlineData("uk-UA", "ru")]
    [InlineData("kk-KZ", "ru")]
    [InlineData("ro-MD", "ru")]
    [InlineData("en-US", "en")]
    [InlineData("de-DE", "en")]
    [InlineData("et-EE", "en")]
    public void LanguageFollowsWindows(string culture, string expected) =>
        Assert.Equal(expected, L.Detect(CultureInfo.GetCultureInfo(culture)));

    [Fact]
    public void ExplicitChoiceWins()
    {
        Assert.Equal("en", L.Resolve("en"));
        Assert.Equal("ru", L.Resolve("ru"));
    }
}
