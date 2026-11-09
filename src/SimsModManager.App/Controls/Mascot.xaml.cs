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

    // Поглаживание: считаем смены направления движения мыши над Коробком
    private double? _lastX;
    private int _direction;
    private readonly Queue<DateTime> _strokes = new();
    private DateTime _lastStroke;
    private DateTime _lastHeart;
    private bool _petting;
    private bool _strokedSinceDown;

    public Mascot()
    {
        InitializeComponent();
        PupilL.Fill = _eyeBrush;
        PupilR.Fill = _eyeBrush;
        foreach (var spark in new[] { Spark1, Spark2, Spark3 }) spark.RenderTransform = new ScaleTransform(0, 0);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _timer.Tick += OnTick;
        MouseLeftButtonDown += (_, _) => _strokedSinceDown = false;
        MouseLeftButtonUp += (_, _) =>
        {
            Activity();
            if (_petting || _strokedSinceDown) return; // гладили, а не нажимали
            Ouch();
            Poked?.Invoke();
        };
        MouseMove += OnPetMove;
        MouseLeave += (_, _) => _lastX = null;
    }

    /// <summary>По Коробку нажали (не погладили).</summary>
    public event Action? Poked;

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
        if (IsBusy || IsExcited || _petting) return;

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
        if (_petting && now - _lastStroke > TimeSpan.FromMilliseconds(700)) StopPetting();
        if (_petting) return;
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

    // ---------- Тычки, головокружение, прыжки по окну ----------

    /// <summary>Тычок: приплющился, зажмурился, качнулся.</summary>
    private void Ouch()
    {
        Squash.BeginAnimation(ScaleTransform.ScaleYProperty, Keys((0, 1), (0.06, 0.78), (0.3, 1)));
        Squash.BeginAnimation(ScaleTransform.ScaleXProperty, Keys((0, 1), (0.06, 1.12), (0.3, 1)));
        var squint = Keys((0, 1), (0.05, 0.25), (0.3, 0.25), (0.4, 1));
        BlinkL.BeginAnimation(ScaleTransform.ScaleYProperty, squint);
        BlinkR.BeginAnimation(ScaleTransform.ScaleYProperty, squint);
        Shake.BeginAnimation(TranslateTransform.XProperty, Keys((0, 0), (0.05, -3), (0.1, 3), (0.15, -2), (0.2, 0)));
    }

    /// <summary>Закружилась голова: крутится вокруг себя и шатается.</summary>
    public void Dizzy()
    {
        Activity();
        var baseAngle = IsWorried ? -7.0 : 0.0;
        var spin = Keys((0, baseAngle), (0.45, baseAngle + 360), (0.6, baseAngle + 350), (0.75, baseAngle + 368), (0.9, baseAngle + 360));
        spin.Completed += (_, _) =>
        {
            Tilt.BeginAnimation(RotateTransform.AngleProperty, null);
            Tilt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(IsWorried ? -7 : 0, TimeSpan.Zero));
        };
        Tilt.BeginAnimation(RotateTransform.AngleProperty, spin);
        WaveFlaps(2);
    }

    /// <summary>Прыжок на новое место: приседает, в полёте вытягивается и клонится по ходу, при приземлении сплющивается.</summary>
    public void Leap(double crouch, double flight, int direction)
    {
        Activity();
        var land = crouch + flight;
        Squash.BeginAnimation(ScaleTransform.ScaleYProperty,
            Keys((0, 1), (crouch, 0.75), (crouch + 0.12, 1.15), (crouch + flight * 0.5, 1.03), (land - 0.05, 1.1), (land, 0.78), (land + 0.12, 1.05), (land + 0.25, 1)));
        Squash.BeginAnimation(ScaleTransform.ScaleXProperty,
            Keys((0, 1), (crouch, 1.15), (crouch + 0.12, 0.9), (crouch + flight * 0.5, 0.98), (land - 0.05, 0.92), (land, 1.16), (land + 0.12, 0.97), (land + 0.25, 1)));
        var baseAngle = IsWorried ? -7.0 : 0.0;
        Tilt.BeginAnimation(RotateTransform.AngleProperty,
            Keys((0, baseAngle), (crouch, baseAngle), (crouch + 0.15, baseAngle + direction * 10), (land - 0.1, baseAngle - direction * 4), (land + 0.2, baseAngle)));
        // Клапаны: прижаты при приседании, машут в полёте
        FlapL.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-15, TimeSpan.FromSeconds(crouch)));
        FlapR.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(15, TimeSpan.FromSeconds(crouch)));
        var flapStart = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(crouch) };
        flapStart.Tick += (_, _) =>
        {
            flapStart.Stop();
            FlapForever(40, 0.12);
        };
        flapStart.Start();
        var stop = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(land) };
        stop.Tick += (_, _) =>
        {
            stop.Stop();
            if (!IsExcited && !IsBusy) StopFlaps();
        };
        stop.Start();
    }

    /// <summary>Хочет что-то сказать: подпрыгивает и машет.</summary>
    public void Announce()
    {
        Activity();
        Jump(10);
        WaveFlaps(2);
    }

    // ---------- Поглаживание ----------

    private void OnPetMove(object sender, MouseEventArgs e)
    {
        var x = e.GetPosition(Root).X;
        if (_lastX is not { } last)
        {
            _lastX = x;
            return;
        }
        var dx = x - last;
        if (Math.Abs(dx) < 2) return;
        _lastX = x;

        var dir = Math.Sign(dx);
        if (dir == _direction) return;
        _direction = dir;

        // Смена направления — один «гладь»
        var now = DateTime.Now;
        _lastStroke = now;
        _strokedSinceDown = true;
        _strokes.Enqueue(now);
        while (_strokes.Count > 0 && now - _strokes.Peek() > TimeSpan.FromSeconds(1.2)) _strokes.Dequeue();

        if (!_petting && _strokes.Count >= 3 && !IsBusy && !IsExcited) StartPetting();
        if (_petting) OnStroke(dir);
    }

    private void StartPetting()
    {
        _petting = true;
        if (_sleeping) WakeUp();
        BlinkL.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        BlinkR.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        var fast = TimeSpan.FromSeconds(0.15);
        foreach (var eye in new UIElement[] { EyeL, EyeR }) eye.BeginAnimation(OpacityProperty, new DoubleAnimation(0, fast));
        foreach (var happy in new UIElement[] { HappyL, HappyR }) happy.BeginAnimation(OpacityProperty, new DoubleAnimation(1, fast));
        foreach (var blush in new UIElement[] { BlushL, BlushR }) blush.BeginAnimation(OpacityProperty, new DoubleAnimation(0.75, TimeSpan.FromSeconds(0.4)));
        // Клапаны расслабленно опускаются
        FlapL.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-12, TimeSpan.FromSeconds(0.3)));
        FlapR.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(12, TimeSpan.FromSeconds(0.3)));
    }

    /// <summary>Каждое движение руки: Коробок чуть приминается, клонится за рукой и иногда выпускает сердечко.</summary>
    private void OnStroke(int direction)
    {
        Squash.BeginAnimation(ScaleTransform.ScaleYProperty, Keys((0, Squash.ScaleY), (0.08, 0.92), (0.25, 1)));
        Squash.BeginAnimation(ScaleTransform.ScaleXProperty, Keys((0, Squash.ScaleX), (0.08, 1.05), (0.25, 1)));
        Tilt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(direction * 4, TimeSpan.FromSeconds(0.2)) { EasingFunction = new QuadraticEase() });

        var now = DateTime.Now;
        if (now - _lastHeart > TimeSpan.FromMilliseconds(300))
        {
            _lastHeart = now;
            SpawnHeart();
        }
    }

    private void SpawnHeart()
    {
        var heart = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M6,11 C-3,4 1,-2 6,2.5 C11,-2 15,4 6,11 Z"),
            Fill = new SolidColorBrush(Color.FromRgb(0xF0, 0x8F, 0xA6)),
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        var move = new TranslateTransform();
        var scale = new ScaleTransform(0.4, 0.4);
        heart.RenderTransform = new TransformGroup { Children = { scale, move } };
        Canvas.SetLeft(heart, 40 + _random.Next(34));
        Canvas.SetTop(heart, 2);
        Root.Children.Add(heart);

        var life = TimeSpan.FromSeconds(1.1);
        var drift = _random.Next(-10, 11);
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -26, life) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, drift, life));
        var grow = new DoubleAnimation(0.4, 1.1, TimeSpan.FromSeconds(0.3)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        var fade = Keys((0, 1), (0.6, 1), (1.1, 0));
        fade.Completed += (_, _) => Root.Children.Remove(heart);
        heart.BeginAnimation(OpacityProperty, fade);
    }

    private void StopPetting()
    {
        _petting = false;
        _strokes.Clear();
        var fast = TimeSpan.FromSeconds(0.2);
        foreach (var eye in new UIElement[] { EyeL, EyeR }) eye.BeginAnimation(OpacityProperty, new DoubleAnimation(1, fast));
        foreach (var happy in new UIElement[] { HappyL, HappyR }) happy.BeginAnimation(OpacityProperty, new DoubleAnimation(0, fast));
        foreach (var blush in new UIElement[] { BlushL, BlushR }) blush.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromSeconds(0.8)));
        Tilt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(IsWorried ? -7 : 0, fast));
        StopFlaps();
        // Довольный подскок и последнее сердечко
        Jump(8);
        SpawnHeart();
        _nextBlink = DateTime.Now.AddSeconds(1.5);
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
