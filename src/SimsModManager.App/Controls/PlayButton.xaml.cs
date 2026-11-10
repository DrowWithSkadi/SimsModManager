using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SimsModManager.App.ViewModels;

namespace SimsModManager.App.Controls;

/// <summary>Плавающая кнопка «Играть». Команды берёт у MainViewModel (DataContext).</summary>
public partial class PlayButton : UserControl
{
    private enum State { Idle, Busy, Running }

    private State _state = State.Idle;

    public PlayButton()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            StartShine();
            ApplyState(animate: false);
        };

        IdleState.MouseEnter += (_, _) => OnHover(true);
        IdleState.MouseLeave += (_, _) => OnHover(false);
        IdleState.PreviewMouseLeftButtonDown += (_, _) => Squeeze();
        IdleState.PreviewMouseLeftButtonUp += (_, _) => Release();
        IdleState.MouseLeave += (_, _) => Release();
        SizeChanged += (_, _) => UpdateProgress(animate: false);
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    // ---------- Свойства ----------

    public static readonly DependencyProperty IsLaunchingProperty = DependencyProperty.Register(
        nameof(IsLaunching), typeof(bool), typeof(PlayButton), new PropertyMetadata(false, (d, _) => ((PlayButton)d).ApplyState(animate: true)));

    public bool IsLaunching { get => (bool)GetValue(IsLaunchingProperty); set => SetValue(IsLaunchingProperty, value); }

    public static readonly DependencyProperty IsRunningProperty = DependencyProperty.Register(
        nameof(IsRunning), typeof(bool), typeof(PlayButton), new PropertyMetadata(false, (d, _) => ((PlayButton)d).ApplyState(animate: true)));

    public bool IsRunning { get => (bool)GetValue(IsRunningProperty); set => SetValue(IsRunningProperty, value); }

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(PlayButton), new PropertyMetadata(0.0, (d, _) => ((PlayButton)d).UpdateProgress(animate: true)));

    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

    // ---------- Нажатия ----------

    private void OnPlay(object sender, RoutedEventArgs e) => Vm?.LaunchGameCommand.Execute(null);

    private void OnMore(object sender, RoutedEventArgs e) => MoreMenu.IsOpen = !MoreMenu.IsOpen;

    private void OnPlayNoMods(object sender, RoutedEventArgs e)
    {
        MoreMenu.IsOpen = false;
        Vm?.LaunchGameCommand.Execute("nomods");
    }

    private void OnPlayNoScripts(object sender, RoutedEventArgs e)
    {
        MoreMenu.IsOpen = false;
        Vm?.LaunchGameCommand.Execute("noscripts");
    }

    private void OnOpenBackups(object sender, RoutedEventArgs e)
    {
        MoreMenu.IsOpen = false;
        Vm?.OpenSaveBackupsCommand.Execute(null);
    }

    // ---------- Состояния ----------

    private void ApplyState(bool animate)
    {
        var next = IsLaunching ? State.Busy : IsRunning ? State.Running : State.Idle;
        if (next == _state && animate) return;
        _state = next;

        Show(IdleState, next == State.Idle, animate);
        Show(BusyState, next == State.Busy, animate);
        Show(RunState, next == State.Running, animate);
        Glow.BeginAnimation(OpacityProperty, new DoubleAnimation(next == State.Running ? 0 : 0.3, TimeSpan.FromSeconds(0.3)));

        if (next == State.Busy)
        {
            Spinner.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.8)) { RepeatBehavior = RepeatBehavior.Forever });
            UpdateProgress(animate: false);
        }
        else Spinner.BeginAnimation(RotateTransform.AngleProperty, null);

        if (next == State.Running)
        {
            var pulse = new DoubleAnimation(0.7, 1.6, TimeSpan.FromSeconds(1.4)) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
            DotPulse.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
            DotPulse.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
            DotHalo.BeginAnimation(OpacityProperty, new DoubleAnimation(0.45, 0, TimeSpan.FromSeconds(1.4)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        else
        {
            DotPulse.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            DotPulse.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            DotHalo.BeginAnimation(OpacityProperty, null);
        }
    }

    private static void Show(UIElement element, bool visible, bool animate)
    {
        if (!animate)
        {
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = visible ? 1 : 0;
            element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        if (visible)
        {
            element.Visibility = Visibility.Visible;
            element.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(0.25)));
        }
        else
        {
            var fade = new DoubleAnimation(0, TimeSpan.FromSeconds(0.2));
            fade.Completed += (_, _) =>
            {
                if (element.Opacity < 0.01) element.Visibility = Visibility.Collapsed;
            };
            element.BeginAnimation(OpacityProperty, fade);
        }
    }

    private void UpdateProgress(bool animate)
    {
        var width = Math.Max(0, ActualWidth * Math.Clamp(Progress, 0, 1));
        if (animate)
            ProgressFill.BeginAnimation(WidthProperty, new DoubleAnimation(width, TimeSpan.FromSeconds(0.4)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        else
        {
            ProgressFill.BeginAnimation(WidthProperty, null);
            ProgressFill.Width = width;
        }
    }

    // ---------- Живость ----------

    /// <summary>Блик раз в ~4 секунды.</summary>
    private void StartShine()
    {
        var shine = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        shine.KeyFrames.Add(new DiscreteDoubleKeyFrame(-1.2, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        shine.KeyFrames.Add(new DiscreteDoubleKeyFrame(-1.2, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5))));
        shine.KeyFrames.Add(new EasingDoubleKeyFrame(1.2, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.4)), new SineEase { EasingMode = EasingMode.EaseInOut }));
        shine.KeyFrames.Add(new DiscreteDoubleKeyFrame(1.2, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(5.0))));
        ShineMove.BeginAnimation(TranslateTransform.XProperty, shine);
    }

    /// <summary>Наведение: кристалл делает оборот, свечение ярче.</summary>
    private void OnHover(bool over)
    {
        Glow.BeginAnimation(OpacityProperty, new DoubleAnimation(over ? 0.6 : 0.3, TimeSpan.FromSeconds(0.25)));
        HoverTint.BeginAnimation(OpacityProperty, new DoubleAnimation(over ? 1 : 0, TimeSpan.FromSeconds(0.2)));
        if (!over) return;
        var spin = new DoubleAnimationUsingKeyFrames();
        const int steps = 20;
        for (var i = 0; i <= steps; i++)
        {
            var t = (double)i / steps;
            var eased = 1 - Math.Pow(1 - t, 3);
            spin.KeyFrames.Add(new LinearDoubleKeyFrame(Math.Cos(eased * 2 * Math.PI), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.6 * t))));
        }
        CrystalSpin.BeginAnimation(ScaleTransform.ScaleXProperty, spin);
    }

    private void Squeeze()
    {
        var down = new DoubleAnimation(0.95, TimeSpan.FromSeconds(0.08));
        Press.BeginAnimation(ScaleTransform.ScaleXProperty, down);
        Press.BeginAnimation(ScaleTransform.ScaleYProperty, down);
    }

    private void Release()
    {
        if (Press.ScaleX >= 0.999) return;
        var up = new DoubleAnimation(1, TimeSpan.FromSeconds(0.4)) { EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 5 } };
        Press.BeginAnimation(ScaleTransform.ScaleXProperty, up);
        Press.BeginAnimation(ScaleTransform.ScaleYProperty, up);
    }
}
