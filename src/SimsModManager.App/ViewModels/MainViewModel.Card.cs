using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimsModManager.Core.Content;

namespace SimsModManager.App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly OriginalsStore _originals = new();

    // ---------- Карточка мода ----------

    [ObservableProperty] private ModCardViewModel? _card;

    /// <summary>Куда вернуться из карточки: в библиотеку или на экран проверки.</summary>
    private AppPage _cardReturnPage = AppPage.Library;

    [RelayCommand]
    private async Task OpenCard(ModItemViewModel? mod)
    {
        if (mod is null || _library is null) return;
        Card = new ModCardViewModel(mod, _library.Paths, !mod.Model.IsTray && _originals.HasBackup(mod.Name));
        if (Page is AppPage.Library or AppPage.Health) _cardReturnPage = Page;
        Page = AppPage.Card;
        await Card.LoadAsync(_cache);
    }

    [RelayCommand]
    private void CloseCard()
    {
        Page = _cardReturnPage;
    }

    [RelayCommand]
    private async Task DeleteFromCard()
    {
        if (Card is null) return;
        var mod = Card.Mod;
        var countBefore = Mods.Count;
        await DeleteMod(mod);
        if (Mods.Count < countBefore || !Mods.Contains(mod)) Page = _cardReturnPage;
    }

    [RelayCommand]
    private async Task RenameMod()
    {
        if (Card is null || _library is null) return;
        var mod = Card.Mod;
        var dialog = new DialogViewModel
        {
            Title = "Дайте моду понятное имя",
            Icon = (System.Windows.Media.Geometry)System.Windows.Application.Current.FindResource("Icon.Pencil"),
            Message = $"Имя «{mod.Name}» изменится в библиотеке и у папки мода. На игру это не влияет.",
            InputLabel = "Название мода",
            InputHint = "Исходное имя: " + mod.Name,
            InputText = mod.Name,
            PrimaryText = "Сохранить",
            SecondaryText = "Отмена",
        };
        if (await ShowDialogAsync(dialog) != DialogResult.Primary) return;
        var newName = dialog.InputText.Trim();
        if (newName.Length == 0 || newName == mod.Name) return;
        if (!mod.Model.IsTray && !await EnsureGameClosedAsync("переименовать мод")) return;

        try
        {
            var oldName = mod.Name;
            var renamed = _library.Rename(mod.Model, newName);
            _originals.Rename(oldName, renamed.Name);
            ShowToast($"Мод переименован в «{renamed.Name}»", Icons.Check);
            SetLastAction("мод переименован");
            await RefreshAsync();
            var fresh = Mods.FirstOrDefault(m => string.Equals(m.Model.Path, renamed.Path, StringComparison.OrdinalIgnoreCase)
                                                 && m.Name == renamed.Name);
            if (fresh is not null) await OpenCard(fresh);
            else Page = AppPage.Library;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await ShowDialogAsync(new DialogViewModel
            {
                Title = "Не удалось переименовать",
                Icon = Icons.Files,
                Message = ex.Message,
                PrimaryText = "Понятно",
            });
        }
    }

    /// <summary>«Вернуть как было у автора» из экрана настройки — для всех модов, которые там настраиваются.</summary>
    [RelayCommand]
    private async Task RestoreFromEditor()
    {
        if (Editor is null) return;
        var mods = Editor.TargetMods.Where(m => m.IsModified).ToList();
        if (mods.Count == 0)
        {
            ShowToast("Эти вещи ещё не менялись — они и так как у автора", Icons.Info);
            return;
        }
        var answer = await ShowDialogAsync(new DialogViewModel
        {
            Title = "Вернуть как было у автора?",
            Icon = (System.Windows.Media.Geometry)System.Windows.Application.Current.FindResource("Icon.RotateCcw"),
            Message = mods.Count == 1
                ? $"Все изменения настроек «{mods[0].Name}» будут отменены."
                : $"Будут отменены изменения в {Text.Plural(mods.Count, "моде", "модах", "модах")}.",
            PrimaryText = "Вернуть",
            SecondaryText = "Отмена",
        });
        if (answer != DialogResult.Primary || !await EnsureGameClosedAsync("изменить моды")) return;

        try
        {
            await Task.Run(() =>
            {
                foreach (var m in mods) _originals.Restore(m.Name, m.Model.Files);
            });
            foreach (var m in mods) m.IsModified = false;
            ShowToast("Авторские настройки восстановлены", Icons.Check);
            SetLastAction("восстановлены авторские настройки");
            Editor = null;
            if (_editorFromCard && Card is not null) await OpenCard(Card.Mod);
            else Page = AppPage.Library;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowErrorAsync("Не удалось вернуть оригинал", "Файлы мода заняты другой программой.", ex);
        }
    }

    [RelayCommand]
    private async Task RestoreOriginal()
    {
        if (Card is null) return;
        var mod = Card.Mod;
        var answer = await ShowDialogAsync(new DialogViewModel
        {
            Title = "Вернуть оригинал?",
            Icon = (System.Windows.Media.Geometry)System.Windows.Application.Current.FindResource("Icon.RotateCcw"),
            Message = $"Все изменения настроек «{mod.Name}» будут отменены — файлы станут такими, как их сделал автор.",
            PrimaryText = "Вернуть",
            SecondaryText = "Отмена",
        });
        if (answer != DialogResult.Primary || !await EnsureGameClosedAsync("изменить моды")) return;

        try
        {
            var files = mod.Model.Files;
            var restored = await Task.Run(() => _originals.Restore(mod.Name, files));
            mod.IsModified = false;
            ShowToast(restored > 0 ? "Оригинал восстановлен" : "Копия оригинала не найдена", Icons.Check);
            SetLastAction("восстановлен оригинал мода");
            await OpenCard(mod);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowErrorAsync("Не удалось вернуть оригинал", "Файлы мода заняты другой программой.", ex);
        }
    }

    // ---------- Настройка одежды ----------

    [ObservableProperty] private ClothingEditorViewModel? _editor;
    private bool _editorFromCard;

    [RelayCommand]
    private void EditItem(CasItemViewModel? item)
    {
        if (item is null) return;
        Editor = new ClothingEditorViewModel([item], Card?.Image ?? item.Mod.Thumbnail);
        _editorFromCard = true;
        Page = AppPage.Editor;
    }

    [RelayCommand]
    private void EditAllItems()
    {
        if (Card is null || Card.Items.Count == 0) return;
        Editor = new ClothingEditorViewModel(Card.Items.ToList(), Card.Image, Card.Mod.Name, Card.Items.Count);
        _editorFromCard = true;
        Page = AppPage.Editor;
    }

    /// <summary>Массовая настройка: вся одежда из выбранных модов.</summary>
    [RelayCommand]
    private async Task EditSelected()
    {
        var mods = SelectedMods().Where(m => !m.IsTray).ToList();
        if (mods.Count == 0) return;
        IsLoading = true;
        var targets = await Task.Run(() => mods
            .SelectMany(m => CasPackageEditor.ReadItems(m.Model.Files).Select(i => new CasItemViewModel(i, m, false)))
            .ToList());
        IsLoading = false;
        UpdateSummary();

        if (targets.Count == 0)
        {
            ShowToast("Среди выбранных модов нет одежды, причёсок или макияжа", Icons.Info);
            return;
        }
        foreach (var t in targets.Take(2)) await t.LoadSwatchesAsync(); // превью для левой колонки
        Editor = new ClothingEditorViewModel(targets, null);
        _editorFromCard = false;
        Page = AppPage.Editor;
    }

    [RelayCommand]
    private async Task CloseEditor()
    {
        if (Editor?.HasChanges == true)
        {
            var answer = await ShowDialogAsync(new DialogViewModel
            {
                Title = "Выйти без сохранения?",
                Icon = Icons.Info,
                Message = "Изменения настроек не сохранены и пропадут.",
                PrimaryText = "Выйти",
                SecondaryText = "Остаться",
            });
            if (answer != DialogResult.Primary) return;
        }
        Page = _editorFromCard && Card is not null ? AppPage.Card : AppPage.Library;
    }

    [ObservableProperty] private bool _isSavingEditor;

    [RelayCommand]
    private async Task SaveEditor()
    {
        if (Editor is null || IsSavingEditor) return;

        if (Editor.Validate() is { } warning)
        {
            var go = await ShowDialogAsync(new DialogViewModel
            {
                Title = "Проверьте настройки",
                Icon = Icons.Alert,
                Message = warning,
                PrimaryText = "Всё равно сохранить",
                SecondaryText = "Исправить",
            });
            if (go != DialogResult.Primary) return;
        }
        if (!await EnsureGameClosedAsync("изменить моды")) return;

        var work = Editor.BuildChanges();
        if (work.Count == 0)
        {
            await CloseEditor();
            return;
        }

        IsSavingEditor = true;
        try
        {
            var originals = _originals;
            var changed = await Task.Run(() =>
            {
                var total = 0;
                foreach (var (target, changes) in work)
                {
                    originals.EnsureBackup(target.Mod.Name, target.Item.PackagePath);
                    total += CasPackageEditor.Save(target.Item.PackagePath, changes);
                }
                return total;
            });

            foreach (var mod in work.Select(w => w.Target.Mod).Distinct()) mod.IsModified = true;
            ShowToast($"Сохранено: {Text.Plural(changed, "расцветка", "расцветки", "расцветок")}. Оригинал сохранён.", Icons.Check);
            SetLastAction("изменены настройки одежды");

            Editor = null;
            if (_editorFromCard && Card is not null) await OpenCard(Card.Mod);
            else Page = AppPage.Library;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            await ShowErrorAsync("Не удалось сохранить", "Файл мода занят или повреждён. Исходный файл не изменён.", ex);
        }
        finally
        {
            IsSavingEditor = false;
        }
    }
}
