using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SimsModManager.Core;
using SimsModManager.Core.Content;

namespace SimsModManager.App.ViewModels;

public sealed partial class ModItemViewModel : ObservableObject
{
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
                parts.Add(Text.Plural(Model.Files.Count, "файл", "файла", "файлов") + " в Tray");
                parts.Add(Text.Size(Model.SizeBytes));
                parts.Add(Model.InstalledAt.ToString("dd.MM.yyyy"));
                return string.Join(" · ", parts);
            }
            if (Model.PackageCount > 0) parts.Add(Text.Plural(Model.PackageCount, "файл", "файла", "файлов") + " .package");
            if (Model.ScriptCount > 0) parts.Add(Text.Plural(Model.ScriptCount, "скрипт", "скрипта", "скриптов"));
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
        ModCategory.Clothing => "Одежда",
        ModCategory.Hair => "Причёски",
        ModCategory.Makeup => "Макияж",
        ModCategory.Objects => "Объекты",
        ModCategory.Scripts => "Скрипты",
        ModCategory.Other => "Другое",
        ModCategory.Tray => ModFiles.TrayKindName(Model.TrayKind),
        _ => "",
    };

    public bool ShowCategory => Category != ModCategory.Unknown;

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
