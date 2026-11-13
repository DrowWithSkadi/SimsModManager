using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SimsModManager.Core;
using SimsModManager.Core.Content;

namespace SimsModManager.App.ViewModels;

public sealed partial class ModItemViewModel : ObservableObject
{
    /// <summary>Перечитать подписи после смены языка.</summary>
    public void RefreshTexts() => OnPropertyChanged(string.Empty);

    private readonly MainViewModel _owner;
    private bool _suppressToggle;

    public ModItemViewModel(ModItem model, MainViewModel owner)
    {
        _owner = owner;
        Model = model;
        _isEnabled = model.IsEnabled;
        _problems = model.Problems;
        if (model.IsTray) _category = ModCategory.Tray;
    }

    public ModItem Model { get; private set; }

    public string Name => Model.Name;
    public bool HasScripts => Model.HasScripts;
    public long SizeBytes => Model.SizeBytes;
    public DateTime InstalledAt => Model.InstalledAt;

    public bool IsTray => Model.IsTray;

    public string DetailsText
    {
        get
        {
            var parts = new List<string>();
            if (Model.IsTray)
            {
                parts.Add(L.P(Model.Files.Count, "файл|файла|файлов") + L.T(" в Tray"));
                parts.Add(Text.Size(Model.SizeBytes));
                parts.Add(Model.InstalledAt.ToString("dd.MM.yyyy"));
                return string.Join(" · ", parts);
            }
            if (Model.PackageCount > 0) parts.Add(L.P(Model.PackageCount, "файл|файла|файлов") + " .package");
            if (Model.ScriptCount > 0) parts.Add(L.P(Model.ScriptCount, "скрипт|скрипта|скриптов"));
            parts.Add(Text.Size(Model.SizeBytes));
            parts.Add(Model.InstalledAt.ToString("dd.MM.yyyy"));
            return string.Join(" · ", parts);
        }
    }

    // ---------- Содержимое (заполняется в фоне) ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CategoryLabel), nameof(ShowCategory), nameof(ShowScriptBadge), nameof(IsScriptOnly))]
    private ModCategory _category = ModCategory.Unknown;

    public string CategoryLabel => Category switch
    {
        ModCategory.Clothing => L.T("Одежда"),
        ModCategory.Hair => L.T("Причёски"),
        ModCategory.Makeup => L.T("Макияж"),
        ModCategory.Objects => L.T("Объекты"),
        ModCategory.Scripts => L.T("Скрипты"),
        ModCategory.Other => L.T("Другое"),
        ModCategory.Tray => ModFiles.TrayKindName(Model.TrayKind),
        _ => "",
    };

    public bool ShowCategory => Category != ModCategory.Unknown;

    // ---------- Дополнения ----------

    /// <summary>Коды дополнений, к которым относятся вещи мода (EP05…).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRequiredPacks), nameof(RequiredPacksLabel), nameof(RequiredPacksTooltip), nameof(MissingPack))]
    private string[] _requiredPacks = [];

    public bool HasRequiredPacks => RequiredPacks.Length > 0;

    public string RequiredPacksLabel => RequiredPacks.Length switch
    {
        0 => "",
        1 => L.T("Нужно: ") + GamePacks.NameOf(RequiredPacks[0]),
        _ => L.T("Нужно: ") + GamePacks.NameOf(RequiredPacks[0]) + " +" + (RequiredPacks.Length - 1),
    };

    /// <summary>Нужного дополнения нет (или оно установлено не полностью) — вещи могут быть невидимыми.</summary>
    public bool MissingPack => RequiredPacks.Any(c => _owner.PackStateOf(c) is PackState.Missing or PackState.Partial);

    public string RequiredPacksTooltip
    {
        get
        {
            if (RequiredPacks.Length == 0) return "";
            var lines = RequiredPacks.Select(c => _owner.PackStateOf(c) switch
            {
                PackState.Installed => $"✓ {GamePacks.NameOf(c)} ({c})",
                PackState.Partial => L.F("! {0} ({1}) — установлено не полностью", GamePacks.NameOf(c), c),
                PackState.Missing => L.F("✗ {0} ({1}) — у вас нет", GamePacks.NameOf(c), c),
                _ => $"• {GamePacks.NameOf(c)} ({c})",
            });
            var text = L.T("Вещи из этого мода сделаны для дополнения:\n") + string.Join("\n", lines);
            return MissingPack ? text + L.T("\n\nБез него вещи могут быть невидимыми или «квадратами».") : text;
        }
    }

    public void RefreshPacks()
    {
        OnPropertyChanged(nameof(MissingPack));
        OnPropertyChanged(nameof(RequiredPacksTooltip));
    }

    /// <summary>Отдельная зелёная метка «Скрипт», если в моде есть скрипты.</summary>
    public bool ShowScriptBadge => HasScripts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThumbnail))]
    private ImageSource? _thumbnail;

    public bool HasThumbnail => Thumbnail is not null;

    /// <summary>Для модов без картинки: скрипт показываем значком кода, остальное — значком картинки.</summary>
    public bool IsScriptOnly => !Model.IsTray && (Model.HasScripts && Model.PackageCount == 0 || Category == ModCategory.Scripts);

    // ---------- Проблемы ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblems), nameof(ProblemsText), nameof(ShowOldWarning))]
    private IReadOnlyList<string> _problems;

    public bool HasProblems => Problems.Count > 0;
    public string ProblemsText => string.Join(Environment.NewLine, Problems);

    [ObservableProperty] private bool _isDuplicate;

    /// <summary>Причины красного флажка (из проверки модов) или null, если всё хорошо.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHealthFlag), nameof(ShowOldWarning))]
    private string? _healthFlag;

    public bool HasHealthFlag => HealthFlag is not null;

    /// <summary>Старый жёлтый значок — только если проверка ещё ничего не сказала.</summary>
    public bool ShowOldWarning => HasProblems && !HasHealthFlag;

    /// <summary>Настройки вещей менялись в менеджере (есть копия оригинала).</summary>
    [ObservableProperty] private bool _isModified;

    // ---------- Состояние строки ----------

    [ObservableProperty] private bool _isSelected;

    /// <summary>Только что установлен — строка плавно «въедет» в список.</summary>
    [ObservableProperty] private bool _isNew;

    /// <summary>Удаляется — строка плавно схлопнется.</summary>
    [ObservableProperty] private bool _isRemoving;

    [ObservableProperty]
    private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppressToggle) return;
        var updated = _owner.TrySetEnabled(this, value);
        if (updated is null)
        {
            SetEnabledSilently(!value);
            return;
        }
        Model = updated;
    }

    /// <summary>Поменять переключатель без перемещения файлов (после массовых действий или отката).</summary>
    internal void SetEnabledSilently(bool value)
    {
        _suppressToggle = true;
        IsEnabled = value;
        _suppressToggle = false;
    }

    internal void ReplaceModel(ModItem model)
    {
        var changed = !ReferenceEquals(Model, model);
        Model = model;
        SetEnabledSilently(model.IsEnabled);
        if (changed) OnPropertyChanged(nameof(DetailsText));
    }
}
