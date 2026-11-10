using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
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

    /// <summary>
    /// Нижние 64 px списка — запас под кнопку «Играть». Строки растворяются над ним (за 48 px),
    /// поэтому список не обрывается ровной линией.
    /// </summary>
    private void OnListAreaSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var height = e.NewSize.Height;
        if (height <= 0) return;
        const double reserve = 64;
        const double fade = 48;
        FadeStart.Offset = Math.Clamp((height - reserve - fade) / height, 0, 1);
        FadeEnd.Offset = Math.Clamp((height - reserve + 6) / height, 0, 1);
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Синхронизируем выбор с моделью и для строк, которые сейчас не видны на экране.
        foreach (var item in e.AddedItems.OfType<ModItemViewModel>()) item.IsSelected = true;
        foreach (var item in e.RemovedItems.OfType<ModItemViewModel>()) item.IsSelected = false;
        if (DataContext is MainViewModel vm && sender is ListBox list)
            vm.SelectedCount = list.SelectedItems.Count;
    }

    /// <summary>Клик по строке (не по кнопкам и переключателям) открывает карточку мода.</summary>
    private void OnItemClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.IsMultiSelect || Keyboard.Modifiers != ModifierKeys.None) return;
        for (var d = e.OriginalSource as DependencyObject; d is not null; d = ParentOf(d))
        {
            if (d is ButtonBase) return; // кнопки, переключатели, галочки — у них своё действие
            if (d is ListBoxItem { DataContext: ModItemViewModel mod })
            {
                vm.OpenCardCommand.Execute(mod);
                return;
            }
            if (d is ListBox) return;
        }
    }

    private static DependencyObject? ParentOf(DependencyObject d) =>
        d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not MainViewModel vm) return;
        if (sender is ListBox { SelectedItem: ModItemViewModel mod })
        {
            vm.OpenCardCommand.Execute(mod);
            e.Handled = true;
        }
    }

    public void SelectAll()
    {
        if (DataContext is MainViewModel { IsGridView: true }) ModGrid.SelectAll();
        else ModList.SelectAll();
    }
}
