using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace SimsModManager.App;

/// <summary>Заставка с кристаллом при запуске. Когда анимация закончилась (или по клику), сообщает Finished.</summary>
public partial class SplashWindow : Window
{
    private bool _finished;

    public SplashWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            // Если анимация почему-то не запустилась — не задерживаем программу
            try { ((Storyboard)Resources["Intro"]).Begin(this, true); }
            catch (Exception) { Finish(); }
        };
    }

    /// <summary>Пора показывать программу.</summary>
    public event Action? Finished;

    private void OnIntroCompleted(object? sender, EventArgs e) => Finish();

    private void OnSkip(object sender, MouseButtonEventArgs e) => Finish();

    private void Finish()
    {
        if (_finished) return;
        _finished = true;
        Finished?.Invoke();
    }

    /// <summary>Плавно гаснет и закрывается.</summary>
    public void FadeOutAndClose()
    {
        try { ((Storyboard)Resources["Intro"]).Stop(this); } catch (Exception) { }
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(250));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }
}
