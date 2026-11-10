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

    private void SetExpanded(bool expand)
    {
        if (_expanded == expand) return;
        _expanded = expand;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var slide = TimeSpan.FromSeconds(expand ? 0.24 : 0.18);
        Panel.BeginAnimation(WidthProperty, new DoubleAnimation(expand ? ExpandedWidth : CollapsedWidth, slide) { EasingFunction = ease });

        // Подписи появляются чуть позже, когда панель уже выехала, и исчезают сразу
        BeginAnimation(LabelOpacityProperty, new DoubleAnimation(expand ? 1 : 0, TimeSpan.FromSeconds(expand ? 0.2 : 0.08))
            { BeginTime = TimeSpan.FromSeconds(expand ? 0.08 : 0) });

        // Квадрат ▶ ↔ полная кнопка «Играть»
        MiniPlayButton.BeginAnimation(OpacityProperty, new DoubleAnimation(expand ? 0 : 1, TimeSpan.FromSeconds(0.15)));
        MiniPlayButton.IsHitTestVisible = !expand;
        FullPlay.BeginAnimation(OpacityProperty, new DoubleAnimation(expand ? 1 : 0, TimeSpan.FromSeconds(expand ? 0.25 : 0.1))
            { BeginTime = TimeSpan.FromSeconds(expand ? 0.06 : 0) });
        FullPlay.IsHitTestVisible = expand;
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
