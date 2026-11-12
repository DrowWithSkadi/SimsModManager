using System.Windows.Controls;
using System.Windows.Input;
using SimsModManager.App.ViewModels;

namespace SimsModManager.App.Views;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    /// <summary>Пока кнопка «Клавиша» ждёт нажатия — следующее нажатие становится клавишей оверлея.</summary>
    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !vm.IsRecordingHotkey) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (vm.TrySetHotkey(key, Keyboard.Modifiers)) e.Handled = true;
        else if (key is not (Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin))
            e.Handled = true; // одиночные буквы не принимаем, но и кнопку ими не нажимаем
    }
}
