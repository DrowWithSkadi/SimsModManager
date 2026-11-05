using System.Windows;
using System.Windows.Threading;

namespace SimsModManager.App;

public partial class App : Application
{
    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Вместо молчаливого вылета показываем ошибку — её текст удобно прислать для исправления.
        MessageBox.Show(e.Exception.ToString(), "Непредвиденная ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
