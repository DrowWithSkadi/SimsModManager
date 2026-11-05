using System.Windows;
using System.Windows.Threading;
using Velopack;

namespace SimsModManager.App;

public partial class App : Application
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Должно идти первым: установщик Velopack запускает программу с особыми ключами
        // при установке, обновлении и удалении (создание и удаление ярлыков).
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        Services.ThemeService.Apply(Core.AppSettings.Load().Theme);
        app.Run();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Вместо молчаливого вылета показываем ошибку — её текст удобно прислать для исправления.
        MessageBox.Show(e.Exception.ToString(), "Непредвиденная ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
