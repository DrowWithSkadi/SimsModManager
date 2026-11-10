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

    private Dispatcher? _splashDispatcher;
    private SplashWindow? _splash;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!Core.AppSettings.Load().ShowSplash)
        {
            ShowMain();
            return;
        }

        // Заставка крутится в отдельном потоке со своим диспетчером: пока главный поток
        // собирает окно программы (это занимает около секунды), анимация не подтормаживает.
        var splashDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var thread = new Thread(() =>
            {
                try
                {
                    _splashDispatcher = Dispatcher.CurrentDispatcher;
                    _splashDispatcher.UnhandledException += (_, args) =>
                    {
                        // Любая ошибка в заставке — просто закрываем её и открываем программу
                        args.Handled = true;
                        splashDone.TrySetResult();
                        Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                    };
                    _splash = new SplashWindow();
                    _splash.Finished += () => splashDone.TrySetResult();
                    _splash.Closed += (_, _) => Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                    _splash.Show();
                    Dispatcher.Run();
                }
                catch (Exception)
                {
                    splashDone.TrySetResult(); // заставка не важнее программы
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
        }
        catch (Exception)
        {
            ShowMain();
            return;
        }

        EnsureMain(); // тяжёлая часть — параллельно с анимацией
        await Task.WhenAny(splashDone.Task, Task.Delay(TimeSpan.FromSeconds(6)));
        ShowMain();
        try { _splashDispatcher?.BeginInvoke(() => _splash?.FadeOutAndClose()); }
        catch (Exception) { }
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
