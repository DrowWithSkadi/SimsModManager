using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SimsModManager.App.Controls;

/// <summary>
/// Коробок — маленький помощник в шапке. Моргает, следит за мышкой, засыпает, если долго ничего не делать.
/// Реагирует на состояние программы через свойства:
///  IsExcited — тащат файлы в окно: подпрыгивает и машет клапанами;
///  IsBusy    — идёт установка: хлопает клапанами и смотрит вниз, после — радуется с искрами;
///  IsWorried — есть проблема: коробка съезжает набок, глаза желтеют, капелька пота.
/// </summary>
public partial class Mascot : UserControl
{
    private static readonly Color EyeCalm = Color.FromRgb(0x77, 0xE1, 0xC7);
    private static readonly Color EyeWorried = Color.FromRgb(0xF5, 0xC7, 0x7E);
    private static readonly TimeSpan SleepAfter = TimeSpan.FromSeconds(90);

    private readonly SolidColorBrush _eyeBrush = new(EyeCalm);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Random _random = new();
    private DateTime _lastActivity = DateTime.Now;
    private DateTime _nextBlink = DateTime.Now.AddSeconds(2);
    private bool _sleeping;
    private Window? _window;

    public Mascot()
    {
        InitializeComponent();
        PupilL.Fill = _eyeBrush;
        PupilR.Fill = _eyeBrush;
        foreach (var spark in new[] { Spark1, Spark2, Spark3 }) spark.RenderTransform = new ScaleTransform(0, 0);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _timer.Tick += OnTick;
        MouseLeftButtonUp += (_, _) =>
        {
            Activity();
            Jump(14);
            WaveFlaps(2);
        };
    }

    // ---------- Свойства-состояния ----------

    public static readonly DependencyProperty IsExcitedProperty = DependencyProperty.Register(
        nameof(IsExcited), typeof(bool), typeof(Mascot), new PropertyMetadata(false, (d, e) => ((Mascot)d).OnExcitedChanged((bool)e.NewValue)));

    public bool IsExcited { get => (bool)GetValue(IsExcitedProperty); set => SetValue(IsExcitedProperty, value); }

    public static readonly DependencyProperty IsBusyProperty = DependencyProperty.Register(
        nameof(IsBusy), typeof(bool), typeof(Mascot), new PropertyMetadata(false, (d, e) => ((Mascot)d).OnBusyChanged((bool)e.NewValue)));

    public bool IsBusy { get => (bool)GetValue(IsBusyProperty); set => SetValue(IsBusyProperty, value); }

    public static readonly DependencyProperty IsWorriedProperty = DependencyProperty.Register(
        nameof(IsWorried), typeof(bool), typeof(Mascot), new PropertyMetadata(false, (d, e) => ((Mascot)d).OnWorriedChanged((bool)e.NewValue)));

    public bool IsWorried { get => (bool)GetValue(IsWorriedProperty); set => SetValue(IsWorriedProperty, value); }

    // ---------- Жизненный цикл ----------

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _window = Window.GetWindow(this);
        if (_window is not null)
        {
            _window.PreviewMouseMove += OnWindowMouseMove;
            _window.PreviewKeyDown += OnWindowKey;
        }
        _timer.Start();
        if (IsWorried) OnWorriedChanged(true);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        if (_window is not null)
        {
            _window.PreviewMouseMove -= OnWindowMouseMove;
            _window.PreviewKeyDown -= OnWindowKey;
        }
    }

    private void OnWindowKey(object sender, KeyEventArgs e) => Activity();

    private void OnWindowMouseMove(object sender, MouseEventArgs e)
    {
        Activity();
        if (IsBusy || IsExcited) return;

        // Зрачки смотрят на курсор (не дальше края глаза)
        var p = e.GetPosition(Body);
        Look(LookL, p, new Point(40, 24));
        Look(LookR, p, new Point(80, 24));
    }

    private static void Look(TranslateTransform pupil, Point mouse, Point eye)
    {
        var dx = mouse.X - eye.X;
        var dy = mouse.Y - eye.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 0.01) return;
        var reach = Math.Min(3.0, length / 25);
        pupil.X = dx / length * reach;
        pupil.Y = dy / length * reach * 1.2;
    }

    private void Activity()
    {
        _lastActivity = DateTime.Now;
        if (_sleeping) WakeUp();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        if (!_sleeping && !IsBusy && !IsExcited && now - _lastActivity > SleepAfter)
        {
            FallAsleep();
            return;
        }
        if (_sleeping) return;

        if (now >= _nextBlink)
        {
            Blink();
            // Иногда моргает дважды подряд
            _nextBlink = now.AddMilliseconds(_random.Next(5) == 0 ? 350 : _random.Next(2500, 6000));
        }
        if (!IsBusy && !IsExcited && _random.Next(120) == 0) WaveFlaps(1);
    }

    // ---------- Движения ----------

    private static DoubleAnimationUsingKeyFrames Keys(params (double Seconds, double Value)[] frames)
    {
        var a = new DoubleAnimationUsingKeyFrames();
        foreach (var (s, v) in frames)
            a.KeyFrames.Add(new EasingDoubleKeyFrame(v, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(s)),
                new QuadraticEase { EasingMode = EasingMode.EaseInOut }));
        return a;
    }

    private void Blink()
    {
        var blink = Keys((0, 1), (0.07, 0.1), (0.15, 1));
        BlinkL.BeginAnimation(ScaleTransform.ScaleYProperty, blink);
        BlinkR.BeginAnimation(ScaleTransform.ScaleYProperty, blink);
    }

    /// <summary>Прыжок: присел, подскочил, приземлился с лёгким сплющиванием.</summary>
    private void Jump(double height)
    {
        Hop.BeginAnimation(TranslateTransform.YProperty, Keys((0, 0), (0.08, 2), (0.26, -height), (0.44, 0), (0.6, 0)));
        Squash.BeginAnimation(ScaleTransform.ScaleYProperty, Keys((0, 1), (0.08, 0.82), (0.2, 1.08), (0.44, 1), (0.5, 0.88), (0.62, 1)));
        Squash.BeginAnimation(ScaleTransform.ScaleXProperty, Keys((0, 1), (0.08, 1.1), (0.2, 0.95), (0.44, 1), (0.5, 1.08), (0.62, 1)));
    }

    private void WaveFlaps(int times)
    {
        var frames = new List<(double, double)> { (0, 0) };
        for (var i = 0; i < times; i++)
        {
            frames.Add((0.12 + i * 0.3, 1));
            frames.Add((0.27 + i * 0.3, 0));
        }
        FlapL.BeginAnimation(RotateTransform.AngleProperty, Keys(frames.Select(f => (f.Item1, f.Item2 * 40)).ToArray()));
        FlapR.BeginAnimation(RotateTransform.AngleProperty, Keys(frames.Select(f => (f.Item1, f.Item2 * -40)).ToArray()));
    }

    private void FlapForever(double angle, double seconds)
    {
        var left = new DoubleAnimation(0, angle, TimeSpan.FromSeconds(seconds)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
        var right = new DoubleAnimation(0, -angle, TimeSpan.FromSeconds(seconds)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
        FlapL.BeginAnimation(RotateTransform.AngleProperty, left);
        FlapR.BeginAnimation(RotateTransform.AngleProperty, right);
    }

    private void StopFlaps()
    {
        FlapL.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, TimeSpan.FromSeconds(0.15)));
        FlapR.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, TimeSpan.FromSeconds(0.15)));
    }

    private void SetPupils(double x, double y)
    {
        LookL.X = LookR.X = x;
        LookL.Y = LookR.Y = y;
    }

    // ---------- Сон ----------

    private void FallAsleep()
    {
        _sleeping = true;
        SetPupils(0, 2);
        var close = new DoubleAnimation(0.15, TimeSpan.FromSeconds(0.6));
        BlinkL.BeginAnimation(ScaleTransform.ScaleYProperty, close);
        BlinkR.BeginAnimation(ScaleTransform.ScaleYProperty, close);
        // Оседает на пол — ножки скрываются
        Hop.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(9, TimeSpan.FromSeconds(0.8)) { EasingFunction = new QuadraticEase() });

        var period = TimeSpan.FromSeconds(2.2);
        Zzz.BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames
        {
            RepeatBehavior = RepeatBehavior.Forever,
            KeyFrames =
            {
                new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.5))),
                new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(period)),
            },
        });
        ZzzMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(4, -16, period) { RepeatBehavior = RepeatBehavior.Forever });
        ZzzMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, 8, period) { RepeatBehavior = RepeatBehavior.Forever });
    }

    private void WakeUp()
    {
        _sleeping = false;
        SetPupils(0, 0);
        Zzz.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromSeconds(0.2)));
        ZzzMove.BeginAnimation(TranslateTransform.YProperty, null);
        ZzzMove.BeginAnimation(TranslateTransform.XProperty, null);
        var open = new DoubleAnimation(1, TimeSpan.FromSeconds(0.15));
        BlinkL.BeginAnimation(ScaleTransform.ScaleYProperty, open);
        BlinkR.BeginAnimation(ScaleTransform.ScaleYProperty, open);
        Jump(6); // встряхнулся
        _nextBlink = DateTime.Now.AddSeconds(1);
    }

    // ---------- Реакции на программу ----------

    private void OnExcitedChanged(bool excited)
    {
        if (excited)
        {
            Activity();
            SetPupils(0, -2.5);
            Hop.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, -7, TimeSpan.FromSeconds(0.18)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new QuadraticEase() });
            FlapForever(45, 0.15);
        }
        else
        {
            SetPupils(0, 0);
            Hop.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromSeconds(0.15)));
            StopFlaps();
        }
    }

    private void OnBusyChanged(bool busy)
    {
        if (busy)
        {
            Activity();
            SetPupils(0, 3); // смотрит вниз — «распаковывает»
            FlapForever(25, 0.1);
        }
        else
        {
            StopFlaps();
            SetPupils(0, 0);
            Celebrate();
        }
    }

    /// <summary>Радость: высокий прыжок, взмахи клапанами и искры вокруг.</summary>
    private void Celebrate()
    {
        Jump(18);
        WaveFlaps(3);
        var delay = 0.0;
        foreach (var spark in new[] { Spark1, Spark2, Spark3 })
        {
            var scale = (ScaleTransform)spark.RenderTransform;
            var pop = Keys((0, 0), (delay, 0), (delay + 0.2, 1.2), (delay + 0.35, 0.8), (delay + 0.55, 1.1), (delay + 0.9, 0));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            spark.BeginAnimation(OpacityProperty, Keys((0, 0), (delay, 0), (delay + 0.1, 1), (delay + 0.7, 1), (delay + 0.9, 0)));
            delay += 0.12;
        }
    }

    private void OnWorriedChanged(bool worried)
    {
        _eyeBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(worried ? EyeWorried : EyeCalm, TimeSpan.FromSeconds(0.3)));
        Tilt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(worried ? -7 : 0, TimeSpan.FromSeconds(0.3)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut } });
        if (worried)
        {
            Shake.BeginAnimation(TranslateTransform.XProperty, Keys((0, 0), (0.05, -2.5), (0.1, 2.5), (0.15, -2), (0.2, 2), (0.25, -1), (0.3, 0)));
            Sweat.BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever,
                KeyFrames =
                {
                    new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                    new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.3))),
                    new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.2))),
                    new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.6))),
                    new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.5))),
                },
            });
            // Капля стекает в такт с появлением (тот же цикл 3.5 с)
            var drop = Keys((0, 0), (1.6, 10), (1.61, 0), (3.5, 0));
            drop.RepeatBehavior = RepeatBehavior.Forever;
            SweatDrop.BeginAnimation(TranslateTransform.YProperty, drop);
        }
        else
        {
            Sweat.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromSeconds(0.2)));
            SweatDrop.BeginAnimation(TranslateTransform.YProperty, null);
        }
    }
}
