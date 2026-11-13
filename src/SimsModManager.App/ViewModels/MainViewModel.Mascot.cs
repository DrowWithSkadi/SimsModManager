using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using SimsModManager.Core;

namespace SimsModManager.App.ViewModels;

public enum NoticeKind { Info, Warning, Error }

/// <summary>Что Коробок говорит в облачке: текст и, если можно исправить, кнопки.</summary>
public sealed record MascotNotice(
    string Key,
    NoticeKind Kind,
    string Title,
    string? Detail,
    string? ActionText = null,
    ICommand? Action = null,
    string? SecondaryText = null,
    ICommand? Secondary = null,
    string? TertiaryText = null,
    ICommand? Tertiary = null)
{
    public bool IsAlert => Kind != NoticeKind.Info;
}

/// <summary>Коробок: собирает предупреждения библиотеки в одно сообщение для облачка (самое важное — первым).</summary>
public sealed partial class MainViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MascotWorried))]
    private MascotNotice? _mascotNotice;

    /// <summary>Есть предупреждение или ошибка — Коробок волнуется.</summary>
    public bool MascotWorried => MascotNotice?.IsAlert == true;

    private bool _mascotHooked;

    private void HookMascot()
    {
        if (_mascotHooked) return;
        _mascotHooked = true;
        PropertyChanged += OnAnyPropertyChangedForMascot;
        UpdateMascotNotice();
    }

    private void OnAnyPropertyChangedForMascot(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MascotNotice) or nameof(MascotWorried)) return;
        UpdateMascotNotice();
    }

    private void UpdateMascotNotice()
    {
        var next = BuildMascotNotice();
        // Не пересоздаём облачко, если смысл не поменялся
        if (next?.Key == MascotNotice?.Key && next?.Detail == MascotNotice?.Detail && next?.Title == MascotNotice?.Title
            && next?.ActionText == MascotNotice?.ActionText) return;
        MascotNotice = next;
    }

    private MascotNotice? BuildMascotNotice()
    {
        if (!HasGame) return null;

        if (HasHealthBanner)
            return CanFixAll
                ? new("health", NoticeKind.Error, L.T("Кажется, с модами беда!"), HealthBannerText + L.T(". Могу исправить сам."),
                    L.T("Исправить всё"), FixAllCommand, L.T("Посмотреть"), OpenHealthCommand, L.T("Не напоминать"), DismissHealthIssuesCommand)
                : new("health", NoticeKind.Error, L.T("Кажется, с модами беда!"), HealthBannerText,
                    L.T("Посмотреть"), OpenHealthCommand, L.T("Не напоминать"), DismissHealthIssuesCommand);

        if (HasOptionsWarning)
            return new("options", NoticeKind.Warning, OptionsWarning + "!",
                OptionsWarningDetail ?? L.T("Игра их сейчас не загрузит. Включить?"),
                L.T("Включить моды"), FixOptionsCommand);

        if (HasGameUpdate)
            return new("game-update", NoticeKind.Warning, L.T("Игра обновилась!"), GameUpdateText,
                L.T("Выключить их"), DisableOutdatedScriptsCommand, L.T("Всё работает"), AcknowledgeGameUpdateCommand);

        if (HasBisect)
            return new("bisect", NoticeKind.Info, L.T("Ищем мод, который мешает игре"), BisectStepText,
                L.T("Продолжить"), OpenHealthCommand);

        if (HasDuplicates)
            return new("duplicates", NoticeKind.Warning, L.T("Нашёл одинаковые моды!"), DuplicateText,
                L.T("Показать"), ShowDuplicatesCommand);

        if (IsGameRunning)
            return new("game-running", NoticeKind.Info, L.T("Игра сейчас запущена"),
                L.T("Изменения применятся, когда перезапустишь игру."));

        return null;
    }
}
