using System.IO;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SimsModManager.Core;

namespace SimsModManager.App.ViewModels;

/// <summary>Строка списка дополнений в настройках.</summary>
public sealed record PackRow(string Code, string Name, PackState State)
{
    public bool IsPartial => State == PackState.Partial;
    public string Tooltip => IsPartial
        ? L.F("{0}: папка есть, но основных файлов нет (только обновления) — вещи из этого дополнения будут невидимыми или «квадратами»", Code)
        : Code;
}

/// <summary>Какие дополнения установлены у игры — для меток «Нужно: …» у модов и подсказок в настройке одежды.</summary>
public sealed partial class MainViewModel
{
    /// <summary>null — игру не нашли, поэтому неизвестно, что установлено.</summary>
    private Dictionary<string, PackState>? _packs;

    public ObservableCollection<PackRow> PackRows { get; } = [];

    [ObservableProperty] private string _packsSummary = L.T("Смотрю, какие дополнения установлены…");
    [ObservableProperty] private bool _hasPartialPacks;

    public bool PacksKnown => _packs is not null;

    /// <summary>Состояние дополнения или null, если игра не найдена.</summary>
    public PackState? PackStateOf(string code) => _packs is null ? null : _packs.GetValueOrDefault(code, PackState.Missing);

    /// <summary>«Времена года» нет или установлены не полностью.</summary>
    public bool SeasonsMissing => PackStateOf(GamePacks.Seasons) is PackState.Missing or PackState.Partial;

    public string SeasonsHint => PackStateOf(GamePacks.Seasons) switch
    {
        PackState.Installed => L.T("Нужно дополнение «Времена года» · установлено"),
        PackState.Partial => L.T("Нужно дополнение «Времена года» · установлено не полностью"),
        PackState.Missing => L.T("Нужно дополнение «Времена года» · у вас его нет"),
        _ => L.T("Нужно дополнение «Времена года»"),
    };

    private async void ScanPacks()
    {
        Dictionary<string, PackState>? packs = null;
        try
        {
            var exe = _settings.GameExe;
            packs = await Task.Run(() =>
            {
                var game = GameLauncher.Find(exe);
                return game is null ? null : GamePacks.Scan(GameLauncher.InstallRoot(game));
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        _packs = packs;
        PackRows.Clear();
        if (packs is null)
        {
            PacksSummary = L.T("Не нашёл, где установлена игра. Укажите её один раз — через кнопку «Играть».");
            HasPartialPacks = false;
        }
        else
        {
            var known = packs.Where(p => p.Value != PackState.Missing)
                .Select(p => new PackRow(p.Key, GamePacks.NameOf(p.Key), p.Value))
                .OrderBy(r => r.Code[..2] switch { "EP" => 0, "GP" => 1, "SP" => 2, _ => 3 })
                .ThenBy(r => r.Code, StringComparer.Ordinal);
            foreach (var row in known) PackRows.Add(row);
            var full = PackRows.Count(r => !r.IsPartial);
            HasPartialPacks = PackRows.Any(r => r.IsPartial);
            PacksSummary = full == 0 && !HasPartialPacks
                ? L.T("Дополнений нет — только базовая игра")
                : L.T("Установлено: ") + L.P(full, "дополнение|дополнения|дополнений")
                  + (HasPartialPacks ? L.T(" · есть установленные не полностью") : "");
        }
        OnPropertyChanged(nameof(PacksKnown));
        OnPropertyChanged(nameof(SeasonsMissing));
        OnPropertyChanged(nameof(SeasonsHint));
        foreach (var mod in Mods) mod.RefreshPacks();
    }
}
