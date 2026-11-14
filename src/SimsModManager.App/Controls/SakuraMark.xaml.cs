using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SimsModManager.App.Controls;

/// <summary>Живая сакура — знак Ancora (см. SakuraMark.xaml).</summary>
public partial class SakuraMark : UserControl
{
    public static readonly DependencyProperty AnimatedProperty = DependencyProperty.Register(
        nameof(Animated), typeof(bool), typeof(SakuraMark), new PropertyMetadata(true, (d, _) => ((SakuraMark)d).Restart()));

    public static readonly DependencyProperty SpinningProperty = DependencyProperty.Register(
        nameof(Spinning), typeof(bool), typeof(SakuraMark), new PropertyMetadata(false, (d, _) => ((SakuraMark)d).StartSpin()));

    public bool Animated { get => (bool)GetValue(AnimatedProperty); set => SetValue(AnimatedProperty, value); }
    public bool Spinning { get => (bool)GetValue(SpinningProperty); set => SetValue(SpinningProperty, value); }

    private bool _loaded;

    public SakuraMark()
    {
        InitializeComponent();
        Loaded += (_, _) => { _loaded = true; Restart(); };
        Unloaded += (_, _) => { _loaded = false; Stop(); };
    }

    private static bool MotionAllowed => SystemParameters.ClientAreaAnimation;

    private void Restart()
    {
        Stop();
        if (!_loaded || !Animated || !MotionAllowed) return;

        // Лепестки дышат: у каждого свой сдвиг по времени, поэтому цветок не пульсирует целиком
        var petals = new[] { Petal0, Petal1, Petal2, Petal3, Petal4 };
        for (var i = 0; i < petals.Length; i++)
        {
            var breathe = new DoubleAnimation(1, 1.045, TimeSpan.FromSeconds(3.25))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(i * 0.55),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            petals[i].BeginAnimation(ScaleTransform.ScaleXProperty, breathe);
            petals[i].BeginAnimation(ScaleTransform.ScaleYProperty, breathe);
        }

        var twinkle = new DoubleAnimation(1, 0.72, TimeSpan.FromSeconds(2.25))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        StarScale.BeginAnimation(ScaleTransform.ScaleXProperty, twinkle);
        StarScale.BeginAnimation(ScaleTransform.ScaleYProperty, twinkle);
        StarTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 45, TimeSpan.FromSeconds(2.25))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
        StartSpin();
    }

    /// <summary>Спокойно — один оборот за 70 секунд; Spinning — за 5 секунд.</summary>
    private void StartSpin()
    {
        if (!_loaded || !Animated || !MotionAllowed) return;
        var from = Spin.Angle % 360;
        Spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(from, from + 360, TimeSpan.FromSeconds(Spinning ? 5 : 70))
        {
            RepeatBehavior = RepeatBehavior.Forever,
        });
    }

    private void Stop()
    {
        foreach (var petal in new[] { Petal0, Petal1, Petal2, Petal3, Petal4 })
        {
            petal.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            petal.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }
        StarScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        StarScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        StarTurn.BeginAnimation(RotateTransform.AngleProperty, null);
        Spin.BeginAnimation(RotateTransform.AngleProperty, null);
    }
}
