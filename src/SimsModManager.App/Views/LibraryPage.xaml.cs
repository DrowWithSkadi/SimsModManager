using System.Windows.Controls;
using SimsModManager.App.ViewModels;

namespace SimsModManager.App.Views;

public partial class LibraryPage : UserControl
{
    public LibraryPage()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is MainViewModel old) old.ClearSelectionRequested -= UnselectAll;
            if (e.NewValue is MainViewModel vm) vm.ClearSelectionRequested += UnselectAll;
        };
    }

    private void UnselectAll()
    {
        ModList.UnselectAll();
        ModGrid.UnselectAll();
    }

    public void FocusSearch()
    {
        Search.Focus();
        Search.SelectAll();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Синхронизируем выбор с моделью и для строк, которые сейчас не видны на экране.
        foreach (var item in e.AddedItems.OfType<ModItemViewModel>()) item.IsSelected = true;
        foreach (var item in e.RemovedItems.OfType<ModItemViewModel>()) item.IsSelected = false;
        if (DataContext is MainViewModel vm && sender is ListBox list)
            vm.SelectedCount = list.SelectedItems.Count;
    }

    public void SelectAll()
    {
        if (DataContext is MainViewModel { IsGridView: true }) ModGrid.SelectAll();
        else ModList.SelectAll();
    }
}
