using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SimsModManager.App.Behaviors;

/// <summary>
/// Плавные анимации, которые подключаются прямо в XAML:
///  Anim.Show — плавно показать/скрыть элемент (прозрачность + лёгкий сдвиг),
///  Anim.SmoothValue — плавно двигать полоску прогресса,
///  Anim.Appear — проявить элемент при появлении на экране.
/// </summary>
public static class Anim
{
    public static readonly Duration Fast = TimeSpan.FromMilliseconds(150);
    public static readonly Duration Normal = TimeSpan.FromMilliseconds(220);
    public static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    public static readonly IEasingFunction EaseIn = new CubicEase { EasingMode = EasingMode.EaseIn };

    static Anim()
    {
        ((Freezable)EaseOut).Freeze();
        ((Freezable)EaseIn).Freeze();
    }

    // ---------- Show ----------

    public static readonly DependencyProperty ShowProperty = DependencyProperty.RegisterAttached(
        "Show", typeof(bool), typeof(Anim), new PropertyMetadata(true, OnShowChanged));

    public static bool GetShow(DependencyObject d) => (bool)d.GetValue(ShowProperty);
    public static void SetShow(DependencyObject d, bool value) => d.SetValue(ShowProperty, value);

    /// <summary>Сдвиг по вертикали при появлении (по умолчанию 8 px; 0 — только прозрачность).</summary>
    public static readonly DependencyProperty OffsetProperty = DependencyProperty.RegisterAttached(
        "Offset", typeof(double), typeof(Anim), new PropertyMetadata(8.0));

    public static double GetOffset(DependencyObject d) => (double)d.GetValue(OffsetProperty);
    public static void SetOffset(DependencyObject d, double value) => d.SetValue(OffsetProperty, value);

    /// <summary>Небольшое увеличение при появлении (для диалогов): 0.96 → 1.</summary>
    public static readonly DependencyProperty ScaleProperty = DependencyProperty.RegisterAttached(
        "Scale", typeof(double), typeof(Anim), new PropertyMetadata(1.0));

    public static double GetScale(DependencyObject d) => (double)d.GetValue(ScaleProperty);
    public static void SetScale(DependencyObject d, double value) => d.SetValue(ScaleProperty, value);

    private static void OnShowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement el) return;
        var show = (bool)e.NewValue;

        // До первого показа окна — без анимации, просто нужное состояние.
        if (!el.IsLoaded)
        {
            el.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            el.Opacity = show ? 1 : 0;
            return;
        }

        var (translate, scale) = EnsureTransforms(el);
        var offset = GetOffset(el);
        var startScale = GetScale(el);

        if (show)
        {
            el.Visibility = Visibility.Visible;
            Animate(el, UIElement.OpacityProperty, 1, Normal, EaseOut);
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offset, 0, Normal) { EasingFunction = EaseOut });
            if (startScale != 1)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(startScale, 1, Normal) { EasingFunction = EaseOut });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(startScale, 1, Normal) { EasingFunction = EaseOut });
            }
        }
        else
        {
            var fade = new DoubleAnimation(0, Fast) { EasingFunction = EaseIn };
            fade.Completed += (_, _) =>
            {
                if (!GetShow(el)) el.Visibility = Visibility.Collapsed;
            };
            el.BeginAnimation(UIElement.OpacityProperty, fade);
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offset / 2, Fast) { EasingFunction = EaseIn });
        }
    }

    private static (TranslateTransform, ScaleTransform) EnsureTransforms(FrameworkElement el)
    {
        if (el.RenderTransform is TransformGroup g && g.Children.Count == 2
            && g.Children[0] is ScaleTransform s && g.Children[1] is TranslateTransform t)
            return (t, s);

        var scale = new ScaleTransform(1, 1);
        var translate = new TranslateTransform();
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        el.RenderTransform = new TransformGroup { Children = { scale, translate } };
        return (translate, scale);
    }

    private static void Animate(UIElement el, DependencyProperty prop, double to, Duration duration, IEasingFunction ease) =>
        el.BeginAnimation(prop, new DoubleAnimation(to, duration) { EasingFunction = ease });

    // ---------- Appear ----------

    /// <summary>Проявить элемент при загрузке: прозрачность 0 → 1 и сдвиг снизу.</summary>
    public static readonly DependencyProperty AppearProperty = DependencyProperty.RegisterAttached(
        "Appear", typeof(bool), typeof(Anim), new PropertyMetadata(false, OnAppearChanged));

    public static bool GetAppear(DependencyObject d) => (bool)d.GetValue(AppearProperty);
    public static void SetAppear(DependencyObject d, bool value) => d.SetValue(AppearProperty, value);

    private static void OnAppearChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement el || e.NewValue is not true) return;
        if (el.IsLoaded) PlayAppear(el);
        else el.Loaded += OnAppearLoaded;
    }

    private static void OnAppearLoaded(object sender, RoutedEventArgs e)
    {
        var el = (FrameworkElement)sender;
        el.Loaded -= OnAppearLoaded;
        PlayAppear(el);
    }

    private static void PlayAppear(FrameworkElement el)
    {
        var (translate, _) = EnsureTransforms(el);
        el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Normal) { EasingFunction = EaseOut });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(GetOffset(el), 0, Normal) { EasingFunction = EaseOut });
    }

    // ---------- SmoothValue ----------

    public static readonly DependencyProperty SmoothValueProperty = DependencyProperty.RegisterAttached(
        "SmoothValue", typeof(double), typeof(Anim), new PropertyMetadata(0.0, OnSmoothValueChanged));

    public static double GetSmoothValue(DependencyObject d) => (double)d.GetValue(SmoothValueProperty);
    public static void SetSmoothValue(DependencyObject d, double value) => d.SetValue(SmoothValueProperty, value);

    private static void OnSmoothValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RangeBase range) return;
        var to = (double)e.NewValue;
        if (to < range.Value) // начали сначала — без анимации назад
        {
            range.BeginAnimation(RangeBase.ValueProperty, null);
            range.Value = to;
            return;
        }
        range.BeginAnimation(RangeBase.ValueProperty,
            new DoubleAnimation(to, TimeSpan.FromMilliseconds(300)) { EasingFunction = EaseOut });
    }
}
