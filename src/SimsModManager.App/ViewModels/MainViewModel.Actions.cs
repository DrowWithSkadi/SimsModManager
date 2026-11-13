using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimsModManager.Core;

namespace SimsModManager.App.ViewModels;

public sealed partial class MainViewModel
{
    // ---------- Диалоги ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDialog))]
    private DialogViewModel? _dialog;

    public bool HasDialog => Dialog is not null;

    /// <summary>Последний показанный диалог: остаётся на экране, пока окно плавно исчезает.</summary>
    [ObservableProperty] private DialogViewModel? _displayedDialog;

    partial void OnDialogChanged(DialogViewModel? value)
    {
        if (value is not null) DisplayedDialog = value;
    }

    public async Task<DialogResult> ShowDialogAsync(DialogViewModel dialog)
    {
        // Если какое-то окно уже открыто — ждём, пока его закроют.
        while (Dialog is not null) await Dialog.Result;
        Dialog = dialog;
        var result = await dialog.Result;
        Dialog = null;
        return result;
    }

    private Task ShowErrorAsync(string title, string message, Exception ex) =>
        ShowDialogAsync(new DialogViewModel
        {
            Title = title,
            Icon = Icons.Error,
            IconIsDanger = true,
            Message = message,
            Details = L.F("{0}: {1}\nВремя: {2:dd.MM.yyyy, HH:mm:ss}", ex.GetType().FullName, ex.Message, DateTime.Now),
            Message2 = L.T("Закройте игру и другие программы, работающие с папкой Mods. Скопируйте подробности для сообщения об ошибке."),
            PrimaryText = L.T("Закрыть"),
            ExtraText = L.T("Скопировать"),
        });

    private async Task<bool> EnsureGameClosedAsync(string action)
    {
        if (!GameOptions.IsGameRunning()) return true;
        IsGameRunning = true;
        await ShowDialogAsync(new DialogViewModel
        {
            Title = L.T("Закройте игру, чтобы ") + action,
            Icon = Icons.Gamepad,
            Message = L.T("The Sims 4 сейчас запущена. Игра может удерживать файлы и перезаписать настройки."),
            Message2 = L.T("Сохраните игру, выйдите на рабочий стол и повторите действие. Ваши изменения не потеряются."),
            PrimaryText = L.T("Понятно"),
        });
        return false;
    }

    // ---------- Уведомления ----------

    public ObservableCollection<ToastViewModel> Toasts { get; } = [];

    public void ShowToast(string message, Geometry icon, Action? undo = null, Action? onExpired = null, int seconds = 4)
    {
        ToastViewModel? toast = null;
        var expiredHandled = false;
        var undone = false;
        void Close(ToastViewModel t)
        {
            if (!t.IsVisible) return;
            t.IsVisible = false; // плавно скрываем, потом убираем
            var remove = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            remove.Tick += (_, _) =>
            {
                remove.Stop();
                Toasts.Remove(t);
            };
            remove.Start();
            if (!expiredHandled && !undone)
            {
                expiredHandled = true;
                onExpired?.Invoke();
            }
        }

        Action? undoWrapper = undo is null ? null : () =>
        {
            undone = true;
            undo();
        };

        toast = new ToastViewModel(message, icon, Close, undoWrapper);
        Toasts.Add(toast);
        while (Toasts.Count(t => t.IsVisible) > 3) Close(Toasts.First(t => t.IsVisible));

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Close(toast);
        };
        timer.Start();
    }

    // ---------- Состояние игры (плашки) ----------

    [ObservableProperty] private bool _isGameRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOptionsWarning))]
    private string? _optionsWarning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOptionsWarningDetail))]
    private string? _optionsWarningDetail;

    public bool HasOptionsWarning => OptionsWarning is not null;
    public bool HasOptionsWarningDetail => OptionsWarningDetail is not null;

    [ObservableProperty] private int _duplicateCount;

    public string DuplicateText => L.T("Найдено ") + L.P(DuplicateCount, "дубликат|дубликата|дубликатов")
                                   + L.T(" — одинаковые файлы в разных модах");
    public bool HasDuplicates => DuplicateCount > 0 && !DuplicatesOnly && !HasHealthBanner;

    partial void OnDuplicateCountChanged(int value)
    {
        OnPropertyChanged(nameof(DuplicateText));
        OnPropertyChanged(nameof(HasDuplicates));
    }

    private void RefreshGameState()
    {
        IsGameRunning = GameOptions.IsGameRunning();
        if (_library is null)
        {
            OptionsWarning = null;
            return;
        }
        var state = GameOptions.ReadModSettings(_library.Paths);
        if (state.AllGood)
        {
            OptionsWarning = null;
            OptionsWarningDetail = null;
        }
        else if (!state.ModsEnabled)
        {
            OptionsWarning = L.T("Моды выключены в настройках игры");
            OptionsWarningDetail = state.ScriptModsEnabled ? null : L.T("Скриптовые моды выключены");
        }
        else
        {
            OptionsWarning = L.T("Скриптовые моды выключены в настройках игры");
            OptionsWarningDetail = L.T("Моды со скриптами работать не будут");
        }
        RefreshSettingsPage();
    }

    [RelayCommand]
    private async Task FixOptions()
    {
        if (_library is null || !await EnsureGameClosedAsync(L.T("изменить настройки"))) return;
        try
        {
            GameOptions.EnableMods(_library.Paths);
            ShowToast(L.T("Моды и скрипты включены в настройках игры"), Icons.Check);
            SetLastAction(L.T("включены моды в игре"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowErrorAsync(L.T("Не удалось изменить настройки"), L.T("Файл Options.ini не получилось сохранить."), ex);
        }
        RefreshGameState();
    }

    [RelayCommand]
    private async Task ClearCache()
    {
        if (_library is null || !await EnsureGameClosedAsync(L.T("очистить кэш"))) return;
        try
        {
            var cleared = GameOptions.ClearThumbnailCache(_library.Paths);
            ShowToast(cleared ? L.T("Кэш очищен") : L.T("Кэш уже пуст"), Icons.Check);
            if (cleared) SetLastAction(L.T("очищен кэш"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowErrorAsync(L.T("Не удалось очистить кэш"), L.T("Файл localthumbcache.package занят или защищён."), ex);
        }
    }

    // ---------- Включение и выключение ----------

    internal ModItem? TrySetEnabled(ModItemViewModel vm, bool enabled)
    {
        if (_library is null) return null;
        try
        {
            var updated = _library.SetEnabled(vm.Model, enabled);
            var text = enabled ? L.F("«{0}» включён", vm.Name) : L.F("«{0}» выключен", vm.Name);
            ShowToast(text, Icons.Check);
            SetLastAction(enabled ? L.T("мод включён") : L.T("мод выключен"));
            if (!enabled) ResolveIssuesFor(vm);
            UpdateSummary(); // живой фильтр сам уберёт мод, если он больше не подходит
            return updated;
        }
        catch (InvalidOperationException ex)
        {
            _ = ShowDialogAsync(new DialogViewModel
            {
                Title = L.T("В папке уже есть мод с таким именем"),
                Icon = Icons.Files,
                Message = L.F("Не удалось {0} «{1}». ", (enabled ? L.T("включить") : L.T("выключить")), vm.Name) + ex.Message,
                Message2 = L.T("Переименуйте один из модов или перенесите его в другую папку, затем повторите."),
                PrimaryText = L.T("Понятно"),
            });
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ShowErrorAsync(L.T("Что-то пошло не так"), L.F("Не удалось {0} «{1}».", (enabled ? L.T("включить") : L.T("выключить")), vm.Name), ex);
            return null;
        }
    }

    [RelayCommand]
    private void EnableSelected() => SetSelectedEnabled(true);

    [RelayCommand]
    private void DisableSelected() => SetSelectedEnabled(false);

    private void SetSelectedEnabled(bool enabled)
    {
        if (_library is null) return;
        var targets = SelectedMods().Where(m => m.IsEnabled != enabled).ToList();
        var failed = 0;
        foreach (var vm in targets)
        {
            try
            {
                vm.ReplaceModel(_library.SetEnabled(vm.Model, enabled));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                failed++;
            }
        }
        var done = targets.Count - failed;
        var word = L.P(done, "мод|мода|модов");
        ShowToast((enabled ? L.T("Включено: ") : L.T("Выключено: ")) + word + (failed > 0 ? L.F(", не получилось: {0}", failed) : ""),
            failed > 0 ? Icons.Alert : Icons.Check);
        SetLastAction((enabled ? L.T("включено ") : L.T("выключено ")) + word);
        UpdateSummary();
    }

    [RelayCommand]
    private async Task EnableAll()
    {
        foreach (var m in Mods) m.IsSelected = !m.IsEnabled;
        SetSelectedEnabled(true);
        ClearSelection();
        await Task.CompletedTask;
    }

    // ---------- Удаление с отменой ----------

    private readonly List<TrashedMod> _pendingDeletes = [];

    [RelayCommand]
    private async Task DeleteMod(ModItemViewModel? mod)
    {
        if (mod is null || _library is null) return;
        var answer = await ShowDialogAsync(new DialogViewModel
        {
            Title = L.F("Удалить мод «{0}»?", mod.Name),
            Icon = Icons.Trash,
            IconIsDanger = true,
            Message = DeleteToRecycleBin ? L.T("Файлы будут перемещены в корзину.") : L.T("Файлы будут удалены насовсем."),
            InfoTitle = mod.Name,
            InfoSubtitle = mod.DetailsText,
            PrimaryText = L.T("Удалить"),
            PrimaryIsDanger = true,
            SecondaryText = L.T("Отмена"),
        });
        if (answer != DialogResult.Primary) return;
        await DeleteAsync([mod]);
    }

    [RelayCommand]
    private async Task DeleteSelected()
    {
        var targets = SelectedMods();
        if (targets.Count == 0 || _library is null) return;
        if (targets.Count == 1)
        {
            await DeleteMod(targets[0]);
            return;
        }

        var files = targets.Sum(m => m.Model.Files.Count);
        var size = targets.Sum(m => m.SizeBytes);
        var answer = await ShowDialogAsync(new DialogViewModel
        {
            Title = L.F("Удалить {0}?", L.P(targets.Count, "мод|мода|модов")),
            Icon = Icons.Trash,
            IconIsDanger = true,
            Message = (DeleteToRecycleBin ? L.T("Все связанные файлы будут перемещены в корзину. ") : L.T("Все связанные файлы будут удалены насовсем. "))
                      + L.F("Это действие затронет {0} общим размером {1}.", L.P(files, "файл|файла|файлов"), Text.Size(size)),
            PrimaryText = L.T("Удалить"),
            PrimaryIsDanger = true,
            SecondaryText = L.T("Отмена"),
        });
        if (answer != DialogResult.Primary) return;
        await DeleteAsync(targets);
    }

    private async Task DeleteAsync(IReadOnlyList<ModItemViewModel> targets)
    {
        if (_library is null) return;
        var library = _library;
        var trashed = new List<(ModItemViewModel Vm, TrashedMod Trash)>();

        foreach (var vm in targets)
        {
            try
            {
                trashed.Add((vm, library.MoveToTrash(vm.Model)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await ShowErrorAsync(L.T("Не удалось удалить"), L.F("Мод «{0}» занят другой программой.", vm.Name), ex);
            }
        }
        if (trashed.Count == 0) return;

        // Строки плавно схлопываются, затем исчезают из списка.
        foreach (var (vm, _) in trashed)
        {
            vm.IsSelected = false;
            vm.IsRemoving = true;
        }
        await Task.Delay(230);
        foreach (var (vm, _) in trashed)
        {
            Mods.Remove(vm);
            ResolveIssuesFor(vm);
        }
        lock (_pendingDeletes) _pendingDeletes.AddRange(trashed.Select(t => t.Trash));
        SelectedCount = 0;
        UpdateSummary();

        var message = trashed.Count == 1 ? L.T("Мод удалён") : L.F("Удалено: {0}", L.P(trashed.Count, "мод|мода|модов"));
        SetLastAction(trashed.Count == 1 ? L.T("мод удалён") : L.T("удалено ") + L.P(trashed.Count, "мод|мода|модов"));

        ShowToast(message, Icons.Check, seconds: 6,
            undo: () =>
            {
                var restored = 0;
                foreach (var (_, t) in trashed)
                {
                    try
                    {
                        library.UndoDelete(t);
                        restored++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { }
                    lock (_pendingDeletes) _pendingDeletes.Remove(t);
                }
                SetLastAction(L.T("удаление отменено"));
                _ = RefreshAsync(trashed.Select(t => t.Vm.Name).ToHashSet());
            },
            onExpired: () =>
            {
                var items = trashed.Select(t => t.Trash).ToList();
                _ = Task.Run(() =>
                {
                    foreach (var t in items)
                    {
                        try { library.FinishDelete(t); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException) { }
                        lock (_pendingDeletes) _pendingDeletes.Remove(t);
                    }
                });
            });
    }

    private void FinishPendingDeletes()
    {
        if (_library is null) return;
        List<TrashedMod> items;
        lock (_pendingDeletes) items = [.. _pendingDeletes];
        foreach (var t in items)
        {
            try { _library.FinishDelete(t); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException) { }
        }
    }
}
