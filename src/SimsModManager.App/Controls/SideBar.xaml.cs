using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SimsModManager.App.Controls;

/// <summary>Боковая панель: свёрнута до иконок, при наведении раздвигается и сдвигает страницу.</summary>
public partial class SideBar : UserControl
{
    public const double CollapsedWidth = 64;
    private const double ExpandedWidth = 220;

    private readonly DispatcherTimer _openTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _closeTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private bool _expanded;

    public SideBar()
    {
        InitializeComponent();
        _openTimer.Tick += (_, _) =>
        {
            _openTimer.Stop();
            SetExpanded(true);
        };
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            if (FullPlay.IsMenuOpen) return; // открыто меню «⌄» — не сворачиваемся под ним
            SetExpanded(false);
        };
        Panel.MouseEnter += (_, _) =>
        {
            _closeTimer.Stop();
            if (!_expanded) _openTimer.Start();
        };
        Panel.MouseLeave += (_, _) =>
        {
            _openTimer.Stop();
            if (_expanded) _closeTimer.Start();
        };
        FullPlay.MenuClosed += () =>
        {
            if (!Panel.IsMouseOver) _closeTimer.Start();
        };
        Loaded += (_, _) => StartMiniShine();
    }

    /// <summary>Прозрачность подписей: 0 — панель свёрнута, 1 — раскрыта.</summary>
    public static readonly DependencyProperty LabelOpacityProperty = DependencyProperty.Register(
        nameof(LabelOpacity), typeof(double), typeof(SideBar), new PropertyMetadata(0.0));

    public double LabelOpacity { get => (double)GetValue(LabelOpacityProperty); set => SetValue(LabelOpacityProperty, value); }

    private const double MiniSize = 48;
    private const double FullPlayWidth = 202;
    private const double FullPlayHeight = 44;

    /// <summary>
    /// Раскрытие: панель плавно раздвигается, подписи проявляются ближе к концу,
    /// квадрат ▶ растягивается вместе с панелью и в конце превращается в полную кнопку «Играть».
    /// Сворачивание — в обратном порядке.
    /// </summary>
    private void SetExpanded(bool expand)
    {
        if (_expanded == expand) return;
        _expanded = expand;

        var duration = TimeSpan.FromSeconds(expand ? 0.38 : 0.3);
        IEasingFunction ease = expand
            ? new PowerEase { Power = 4, EasingMode = EasingMode.EaseOut }
            : new CubicEase { EasingMode = EasingMode.EaseInOut };
        DoubleAnimation Anim(double to, TimeSpan time, double delay = 0, IEasingFunction? e = null) =>
            new(to, time) { BeginTime = TimeSpan.FromSeconds(delay), EasingFunction = e };

        Panel.BeginAnimation(WidthProperty, Anim(expand ? ExpandedWidth : CollapsedWidth, duration, 0, ease));

        // Подписи: при раскрытии — когда панель почти выехала, при сворачивании — сразу
        BeginAnimation(LabelOpacityProperty, expand
            ? Anim(1, TimeSpan.FromSeconds(0.22), 0.14)
            : Anim(0, TimeSpan.FromSeconds(0.1)));

        var triangle = MiniPlayButton.Template.FindName("Triangle", MiniPlayButton) as UIElement;
        var sparkle = MiniPlayButton.Template.FindName("Sparkle", MiniPlayButton) as UIElement;

        if (expand)
        {
            // Квадрат растягивается вместе с панелью, значки на нём гаснут
            MiniPlayButton.BeginAnimation(OpacityProperty, null);
            MiniPlayButton.Opacity = 1;
            MiniPlayButton.BeginAnimation(WidthProperty, Anim(FullPlayWidth, duration, 0, ease));
            MiniPlayButton.BeginAnimation(HeightProperty, Anim(FullPlayHeight, duration, 0, ease));
            triangle?.BeginAnimation(OpacityProperty, Anim(0, TimeSpan.FromSeconds(0.12)));
            sparkle?.BeginAnimation(OpacityProperty, Anim(0, TimeSpan.FromSeconds(0.12)));
            MiniPlayButton.IsHitTestVisible = false;

            // В конце поверх проявляется полная кнопка, а растянутый квадрат под ней исчезает
            var appear = duration.TotalSeconds * 0.7;
            FullPlay.BeginAnimation(OpacityProperty, Anim(1, TimeSpan.FromSeconds(0.18), appear));
            MiniPlayButton.BeginAnimation(OpacityProperty, Anim(0, TimeSpan.FromSeconds(0.1), appear + 0.18));
            FullPlay.IsHitTestVisible = true;
        }
        else
        {
            // Полная кнопка тает, под ней — растянутый квадрат, который сжимается обратно
            FullPlay.BeginAnimation(OpacityProperty, Anim(0, TimeSpan.FromSeconds(0.1)));
            FullPlay.IsHitTestVisible = false;
            MiniPlayButton.BeginAnimation(OpacityProperty, Anim(1, TimeSpan.FromSeconds(0.06)));
            MiniPlayButton.BeginAnimation(WidthProperty, Anim(MiniSize, duration, 0.04, ease));
            MiniPlayButton.BeginAnimation(HeightProperty, Anim(MiniSize, duration, 0.04, ease));
            triangle?.BeginAnimation(OpacityProperty, Anim(1, TimeSpan.FromSeconds(0.15), duration.TotalSeconds * 0.6));
            sparkle?.BeginAnimation(OpacityProperty, Anim(0.55, TimeSpan.FromSeconds(0.15), duration.TotalSeconds * 0.6));
            MiniPlayButton.IsHitTestVisible = true;
        }
    }

    /// <summary>Блик по квадратной кнопке раз в несколько секунд.</summary>
    private void StartMiniShine()
    {
        MiniPlayButton.ApplyTemplate();
        if (MiniPlayButton.Template.FindName("MiniShine", MiniPlayButton) is not TranslateTransform shine) return;
        var a = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        a.KeyFrames.Add(new DiscreteDoubleKeyFrame(-1.2, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        a.KeyFrames.Add(new DiscreteDoubleKeyFrame(-1.2, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2))));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1.2, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.7)), new SineEase { EasingMode = EasingMode.EaseInOut }));
        a.KeyFrames.Add(new DiscreteDoubleKeyFrame(1.2, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(5))));
        shine.BeginAnimation(TranslateTransform.XProperty, a);

        if (MiniPlayButton.Template.FindName("MiniSpinner", MiniPlayButton) is RotateTransform spinner)
            spinner.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.8)) { RepeatBehavior = RepeatBehavior.Forever });
    }
}
