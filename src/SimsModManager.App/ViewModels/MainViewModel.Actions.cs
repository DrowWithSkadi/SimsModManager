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
            Details = $"{ex.GetType().FullName}: {ex.Message}\nВремя: {DateTime.Now:dd.MM.yyyy, HH:mm:ss}",
            Message2 = "Закройте игру и другие программы, работающие с папкой Mods. Скопируйте подробности для сообщения об ошибке.",
            PrimaryText = "Закрыть",
            ExtraText = "Скопировать",
        });

    private async Task<bool> EnsureGameClosedAsync(string action)
    {
        if (!GameOptions.IsGameRunning()) return true;
        IsGameRunning = true;
        await ShowDialogAsync(new DialogViewModel
        {
            Title = "Закройте игру, чтобы " + action,
            Icon = Icons.Gamepad,
            Message = "The Sims 4 сейчас запущена. Игра может удерживать файлы и перезаписать настройки.",
            Message2 = "Сохраните игру, выйдите на рабочий стол и повторите действие. Ваши изменения не потеряются.",
            PrimaryText = "Понятно",
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

    public string DuplicateText => "Найдено " + Text.Plural(DuplicateCount, "дубликат", "дубликата", "дубликатов")
                                   + " — одинаковые файлы в разных модах";
    public bool HasDuplicates => DuplicateCount > 0 && !DuplicatesOnly;

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
            OptionsWarning = "Моды выключены в настройках игры";
            OptionsWarningDetail = state.ScriptModsEnabled ? null : "Скриптовые моды выключены";
        }
        else
        {
            OptionsWarning = "Скриптовые моды выключены в настройках игры";
            OptionsWarningDetail = "Моды со скриптами работать не будут";
        }
        RefreshSettingsPage();
    }

    [RelayCommand]
    private async Task FixOptions()
    {
        if (_library is null || !await EnsureGameClosedAsync("изменить настройки")) return;
        try
        {
            GameOptions.EnableMods(_library.Paths);
            ShowToast("Моды и скрипты включены в настройках игры", Icons.Check);
            SetLastAction("включены моды в игре");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowErrorAsync("Не удалось изменить настройки", "Файл Options.ini не получилось сохранить.", ex);
        }
        RefreshGameState();
    }

    [RelayCommand]
    private async Task ClearCache()
    {
        if (_library is null || !await EnsureGameClosedAsync("очистить кэш")) return;
        try
        {
            var cleared = GameOptions.ClearThumbnailCache(_library.Paths);
            ShowToast(cleared ? "Кэш очищен" : "Кэш уже пуст", Icons.Check);
            if (cleared) SetLastAction("очищен кэш");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowErrorAsync("Не удалось очистить кэш", "Файл localthumbcache.package занят или защищён.", ex);
        }
    }

    // ---------- Включение и выключение ----------

    internal ModItem? TrySetEnabled(ModItemViewModel vm, bool enabled)
    {
        if (_library is null) return null;
        try
        {
            var updated = _library.SetEnabled(vm.Model, enabled);
            var text = enabled ? $"«{vm.Name}» включён" : $"«{vm.Name}» выключен";
            ShowToast(text, Icons.Check);
            SetLastAction(enabled ? "мод включён" : "мод выключен");
            Application.Current.Dispatcher.InvokeAsync(RefreshView, DispatcherPriority.Background);
            return updated;
        }
        catch (InvalidOperationException ex)
        {
            _ = ShowDialogAsync(new DialogViewModel
            {
                Title = "В папке уже есть мод с таким именем",
                Icon = Icons.Files,
                Message = $"Не удалось {(enabled ? "включить" : "выключить")} «{vm.Name}». " + ex.Message,
                Message2 = "Переименуйте один из модов или перенесите его в другую папку, затем повторите.",
                PrimaryText = "Понятно",
            });
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ShowErrorAsync("Что-то пошло не так", $"Не удалось {(enabled ? "включить" : "выключить")} «{vm.Name}».", ex);
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
        var word = Text.Plural(done, "мод", "мода", "модов");
        ShowToast((enabled ? "Включено: " : "Выключено: ") + word + (failed > 0 ? $", не получилось: {failed}" : ""),
            failed > 0 ? Icons.Alert : Icons.Check);
        SetLastAction((enabled ? "включено " : "выключено ") + word);
        RefreshView();
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
            Title = $"Удалить мод «{mod.Name}»?",
            Icon = Icons.Trash,
            IconIsDanger = true,
            Message = DeleteToRecycleBin ? "Файлы будут перемещены в корзину." : "Файлы будут удалены насовсем.",
            InfoTitle = mod.Name,
            InfoSubtitle = mod.DetailsText,
            PrimaryText = "Удалить",
            PrimaryIsDanger = true,
            SecondaryText = "Отмена",
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
            Title = $"Удалить {Text.Plural(targets.Count, "мод", "мода", "модов")}?",
            Icon = Icons.Trash,
            IconIsDanger = true,
            Message = (DeleteToRecycleBin ? "Все связанные файлы будут перемещены в корзину. " : "Все связанные файлы будут удалены насовсем. ")
                      + $"Это действие затронет {Text.Plural(files, "файл", "файла", "файлов")} общим размером {Text.Size(size)}.",
            PrimaryText = "Удалить",
            PrimaryIsDanger = true,
            SecondaryText = "Отмена",
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
                await ShowErrorAsync("Не удалось удалить", $"Мод «{vm.Name}» занят другой программой.", ex);
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
        foreach (var (vm, _) in trashed) Mods.Remove(vm);
        lock (_pendingDeletes) _pendingDeletes.AddRange(trashed.Select(t => t.Trash));
        SelectedCount = 0;
        RefreshView();

        var message = trashed.Count == 1 ? "Мод удалён" : $"Удалено: {Text.Plural(trashed.Count, "мод", "мода", "модов")}";
        SetLastAction(trashed.Count == 1 ? "мод удалён" : "удалено " + Text.Plural(trashed.Count, "мод", "мода", "модов"));

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
                SetLastAction("удаление отменено");
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
