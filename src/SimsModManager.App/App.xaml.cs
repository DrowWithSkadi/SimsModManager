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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!Core.AppSettings.Load().ShowSplash)
        {
            ShowMain();
            return;
        }

        // Сначала заставка; главное окно создаём, когда заставка уже на экране.
        var splash = new SplashWindow();
        splash.Finished += () =>
        {
            ShowMain();
            splash.FadeOutAndClose();
        };
        splash.Show();
        Dispatcher.InvokeAsync(EnsureMain, DispatcherPriority.ApplicationIdle);
    }

    private MainWindow? _main;

    private void EnsureMain()
    {
        if (_main is not null) return;
        _main = new MainWindow();
        MainWindow = _main;
    }

    private void ShowMain()
    {
        EnsureMain();
        _main!.Show();
        _main.Activate();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Вместо молчаливого вылета показываем ошибку — её текст удобно прислать для исправления.
        MessageBox.Show(e.Exception.ToString(), "Непредвиденная ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
