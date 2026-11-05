using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using SimsModManager.App.ViewModels;

namespace SimsModManager.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyWindowsStyle();
        StateChanged += (_, _) => OnStateChanged();
        SizeChanged += (_, _) => ViewModel.IsCompact = ActualWidth < 1000;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    // ---------- Рамка окна ----------

    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void OnMaximize(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    private void OnClose(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    private void OnStateChanged()
    {
        // Развёрнутое окно без системной рамки вылезает за экран на толщину рамки — компенсируем.
        RootBorder.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        MaxButton.ToolTip = WindowState == WindowState.Maximized ? "Восстановить" : "Развернуть";
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Скругление углов (Windows 11) и тёмная системная рамка.</summary>
    private void ApplyWindowsStyle()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        try
        {
            var dark = 1;
            DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));      // DWMWA_USE_IMMERSIVE_DARK_MODE
            var round = 2;
            DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));     // DWMWA_WINDOW_CORNER_PREFERENCE = ROUND
            var border = 0x00414141;
            DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));    // DWMWA_BORDER_COLOR (0x00BBGGRR)
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // На старых Windows этих настроек нет — окно просто будет с прямыми углами.
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        ViewModel.Shutdown();
        base.OnClosing(e);
    }

    // ---------- Клавиатура ----------

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = ViewModel;
        if (vm.Dialog is { } dialog)
        {
            if (e.Key == Key.Escape) dialog.CancelCommand.Execute(null);
            else if (e.Key == Key.Enter) dialog.PrimaryCommand.Execute(null);
            else return;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control && vm.ShowLibraryChrome)
        {
            Library.FocusSearch();
            e.Handled = true;
        }
        else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control && vm.IsLibraryPage
                 && Keyboard.FocusedElement is not System.Windows.Controls.TextBox)
        {
            Library.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (vm.IsEditorPage) vm.CloseEditorCommand.Execute(null);
            else if (vm.IsCardPage) vm.CloseCardCommand.Execute(null);
            else if (vm.IsSettingsPage || vm.IsInstallResultPage) vm.GoLibraryCommand.Execute(null);
            else if (vm.SelectedCount > 0) vm.ClearSelectionCommand.Execute(null);
            else return;
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && vm.SelectedCount > 0 && vm.IsLibraryPage
                 && Keyboard.FocusedElement is not System.Windows.Controls.TextBox)
        {
            vm.DeleteSelectedCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ---------- Диалог: клик по затемнению закрывает окно ----------

    private void OnOverlayClick(object sender, MouseButtonEventArgs e) => ViewModel.Dialog?.CancelCommand.Execute(null);

    private void OnDialogClick(object sender, MouseButtonEventArgs e) => e.Handled = true;

    // ---------- Перетаскивание файлов ----------

    private static string[] DroppedFiles(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files ? files : [];

    private void OnDragEnter(object sender, DragEventArgs e) => ViewModel.SetDragFiles(DroppedFiles(e).Length);

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedFiles(e).Length > 0 && !ViewModel.IsInstalling && ViewModel.HasGame
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        // DragLeave приходит и при переходе между элементами окна — прячем, только если курсор правда ушёл.
        var p = e.GetPosition(this);
        if (p.X <= 0 || p.Y <= 0 || p.X >= ActualWidth || p.Y >= ActualHeight)
            ViewModel.SetDragFiles(0);
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        var files = DroppedFiles(e);
        ViewModel.SetDragFiles(0);
        if (files.Length > 0) await ViewModel.InstallAsync(files);
    }
}
