using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SimsModManager.App.ViewModels;

namespace SimsModManager.App.Views;

public partial class LibraryPage : UserControl
{
    // ---------- Плитки: ширина под окно ----------

    private const double TileMinWidth = 188;   // плитка 176 + отступ 12
    private const double TileMargin = 12;      // отступ справа и снизу у плитки
    private const double TilePadding = 10;     // внутренний отступ плитки
    private const double ImageRatio = 132.0 / 156.0;

    public static readonly DependencyProperty TileItemWidthProperty = DependencyProperty.Register(
        nameof(TileItemWidth), typeof(double), typeof(LibraryPage), new PropertyMetadata(TileMinWidth));

    /// <summary>Ширина места под одну плитку (вместе с отступом).</summary>
    public double TileItemWidth { get => (double)GetValue(TileItemWidthProperty); set => SetValue(TileItemWidthProperty, value); }

    public static readonly DependencyProperty TileImageHeightProperty = DependencyProperty.Register(
        nameof(TileImageHeight), typeof(double), typeof(LibraryPage), new PropertyMetadata(132.0));

    public double TileImageHeight { get => (double)GetValue(TileImageHeightProperty); set => SetValue(TileImageHeightProperty, value); }

    /// <summary>
    /// Сколько колонок влезает — столько и ставим, а оставшееся место делим между ними поровну,
    /// чтобы справа не оставалась пустая полоса.
    /// </summary>
    private void OnGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var available = e.NewSize.Width - TileMargin /* слева */ - 14 /* полоса прокрутки */;
        if (available <= 0) return;
        var columns = Math.Max(1, (int)Math.Floor(available / TileMinWidth));
        var item = Math.Floor(available / columns);
        if (Math.Abs(item - TileItemWidth) < 0.5) return;
        TileItemWidth = item;
        TileImageHeight = Math.Round((item - TileMargin - TilePadding * 2) * ImageRatio);
    }

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
