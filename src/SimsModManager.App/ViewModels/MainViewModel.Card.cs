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

    [RelayCommand]
    private async Task OpenCard(ModItemViewModel? mod)
    {
        if (mod is null || _library is null) return;
        Card = new ModCardViewModel(mod, _library.Paths, !mod.Model.IsTray && _originals.HasBackup(mod.Name));
        Page = AppPage.Card;
        await Card.LoadAsync(_cache);
    }

    [RelayCommand]
    private void CloseCard()
    {
        Page = AppPage.Library;
    }

    [RelayCommand]
    private async Task DeleteFromCard()
    {
        if (Card is null) return;
        var mod = Card.Mod;
        var countBefore = Mods.Count;
        await DeleteMod(mod);
        if (Mods.Count < countBefore || !Mods.Contains(mod)) Page = AppPage.Library;
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
        Editor = new ClothingEditorViewModel(Card.Items.ToList(), Card.Image);
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
