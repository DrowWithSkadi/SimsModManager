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
        ((Freezable)Breathe).Freeze();
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

    // ---------- Живые анимации: подсветка, бегущий пунктир, круги, покачивание ----------

    private static readonly IEasingFunction Breathe = new SineEase { EasingMode = EasingMode.EaseInOut };

    /// <summary>
    /// Пульсирующая подсветка: пока true, элемент «дышит» — прозрачность 0.45 ↔ 1 и лёгкое увеличение.
    /// Когда false — плавно гаснет до 0. Ставить на отдельный слой-подсветку (у него в XAML Opacity="0").
    /// </summary>
    public static readonly DependencyProperty GlowProperty = DependencyProperty.RegisterAttached(
        "Glow", typeof(bool), typeof(Anim), new PropertyMetadata(false, OnGlowChanged));

    public static bool GetGlow(DependencyObject d) => (bool)d.GetValue(GlowProperty);
    public static void SetGlow(DependencyObject d, bool value) => d.SetValue(GlowProperty, value);

    /// <summary>Насколько увеличивается подсветка на вдохе (1.02 — на 2%).</summary>
    public static readonly DependencyProperty GlowScaleProperty = DependencyProperty.RegisterAttached(
        "GlowScale", typeof(double), typeof(Anim), new PropertyMetadata(1.02));

    public static double GetGlowScale(DependencyObject d) => (double)d.GetValue(GlowScaleProperty);
    public static void SetGlowScale(DependencyObject d, double value) => d.SetValue(GlowScaleProperty, value);

    private static void OnGlowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement el) return;
        var (_, scale) = EnsureTransforms(el);
        var period = TimeSpan.FromMilliseconds(900);

        if (e.NewValue is true)
        {
            var grow = new DoubleAnimation(1, GetGlowScale(el), period)
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = Breathe };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);

            // Сначала быстро проявляемся, потом начинаем дышать
            var fadeIn = new DoubleAnimation(0, 1, Fast) { EasingFunction = EaseOut };
            fadeIn.Completed += (_, _) =>
            {
                if (GetGlow(el))
                    el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0.45, period)
                        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = Breathe });
            };
            el.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }
        else
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, Normal) { EasingFunction = EaseOut });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, Normal) { EasingFunction = EaseOut });
            el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, Normal) { EasingFunction = EaseOut });
        }
    }

    /// <summary>Бегущий пунктир по рамке (Rectangle/Ellipse с StrokeDashArray), пока true.</summary>
    public static readonly DependencyProperty MarchProperty = DependencyProperty.RegisterAttached(
        "March", typeof(bool), typeof(Anim), new PropertyMetadata(false, OnMarchChanged));

    public static bool GetMarch(DependencyObject d) => (bool)d.GetValue(MarchProperty);
    public static void SetMarch(DependencyObject d, bool value) => d.SetValue(MarchProperty, value);

    private static void OnMarchChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not System.Windows.Shapes.Shape shape) return;
        if (e.NewValue is true)
        {
            // Сдвиг на один полный шаг узора — тогда повтор бесшовный
            var step = shape.StrokeDashArray is { Count: > 0 } dashes ? dashes.Sum() : 10;
            shape.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty,
                new DoubleAnimation(0, -step, TimeSpan.FromMilliseconds(step * 70)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        else
        {
            shape.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, null);
        }
    }

    /// <summary>Расходящиеся круги (как от капли на воде), пока true. Задержку для второго круга — через RippleDelay.</summary>
    public static readonly DependencyProperty RippleProperty = DependencyProperty.RegisterAttached(
        "Ripple", typeof(bool), typeof(Anim), new PropertyMetadata(false, OnRippleChanged));

    public static bool GetRipple(DependencyObject d) => (bool)d.GetValue(RippleProperty);
    public static void SetRipple(DependencyObject d, bool value) => d.SetValue(RippleProperty, value);

    /// <summary>Задержка начала круга в миллисекундах.</summary>
    public static readonly DependencyProperty RippleDelayProperty = DependencyProperty.RegisterAttached(
        "RippleDelay", typeof(double), typeof(Anim), new PropertyMetadata(0.0));

    public static double GetRippleDelay(DependencyObject d) => (double)d.GetValue(RippleDelayProperty);
    public static void SetRippleDelay(DependencyObject d, double value) => d.SetValue(RippleDelayProperty, value);

    private static void OnRippleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement el) return;
        var (_, scale) = EnsureTransforms(el);
        if (e.NewValue is true)
        {
            var duration = TimeSpan.FromMilliseconds(1800);
            var delay = TimeSpan.FromMilliseconds(GetRippleDelay(el));
            var grow = new DoubleAnimation(0.6, 1.9, duration)
                { BeginTime = delay, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = EaseOut };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            el.Opacity = 0;
            el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.55, 0, duration)
                { BeginTime = delay, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = EaseOut });
        }
        else
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            el.BeginAnimation(UIElement.OpacityProperty, null);
            el.Opacity = 0;
        }
    }

    /// <summary>Мягкое покачивание вверх-вниз (для иконки), пока true.</summary>
    public static readonly DependencyProperty BobProperty = DependencyProperty.RegisterAttached(
        "Bob", typeof(bool), typeof(Anim), new PropertyMetadata(false, OnBobChanged));

    public static bool GetBob(DependencyObject d) => (bool)d.GetValue(BobProperty);
    public static void SetBob(DependencyObject d, bool value) => d.SetValue(BobProperty, value);

    private static void OnBobChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement el) return;
        var (translate, _) = EnsureTransforms(el);
        translate.BeginAnimation(TranslateTransform.YProperty, e.NewValue is true
            ? new DoubleAnimation(0, -6, TimeSpan.FromMilliseconds(700))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = Breathe }
            : new DoubleAnimation(0, Fast) { EasingFunction = EaseOut });
    }

    // ---------- Wave: карточки появляются волной ----------

    /// <summary>
    /// Ставится на контейнер элемента списка (ListBoxItem). Когда список показывается (открыли библиотеку,
    /// переключили вид), видимые карточки проявляются по очереди, снизу вверх. При поиске и прокрутке — без анимации.
    /// </summary>
    public static readonly DependencyProperty WaveProperty = DependencyProperty.RegisterAttached(
        "Wave", typeof(bool), typeof(Anim), new PropertyMetadata(false, OnWaveChanged));

    public static bool GetWave(DependencyObject d) => (bool)d.GetValue(WaveProperty);
    public static void SetWave(DependencyObject d, bool value) => d.SetValue(WaveProperty, value);

    private sealed class WaveState
    {
        public DateTime Start = DateTime.Now;
        public int Count;
    }

    private static readonly DependencyProperty WaveStateProperty = DependencyProperty.RegisterAttached(
        "WaveState", typeof(WaveState), typeof(Anim));

    private const int WaveMaxItems = 14;
    private static readonly TimeSpan WaveWindow = TimeSpan.FromMilliseconds(600);

    private static void OnWaveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement item) return;
        item.Loaded -= OnWaveItemLoaded;
        if (e.NewValue is true) item.Loaded += OnWaveItemLoaded;
    }

    private static void OnWaveItemLoaded(object sender, RoutedEventArgs e)
    {
        var item = (FrameworkElement)sender;
        if (System.Windows.Controls.ItemsControl.ItemsControlFromItemContainer(item) is not { } list) return;
        var state = EnsureWaveState(list);
        if (DateTime.Now - state.Start < WaveWindow) PlayWave(item, state.Count++);
    }

    private static WaveState EnsureWaveState(System.Windows.Controls.ItemsControl list)
    {
        if (list.GetValue(WaveStateProperty) is WaveState existing) return existing;
        var state = new WaveState();
        list.SetValue(WaveStateProperty, state);
        list.IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is not true) return;
            state.Start = DateTime.Now;
            state.Count = 0;
            // Карточки, которые уже созданы, проигрываем по порядку, когда разметка готова
            list.Dispatcher.InvokeAsync(() =>
            {
                for (var i = 0; i < list.Items.Count && state.Count < WaveMaxItems; i++)
                    if (list.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement { IsLoaded: true, IsVisible: true } c)
                        PlayWave(c, state.Count++);
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        };
        return state;
    }

    private static void PlayWave(FrameworkElement item, int order)
    {
        if (order >= WaveMaxItems) return;
        var (translate, _) = EnsureTransforms(item);
        var delay = TimeSpan.FromMilliseconds(order * 35);
        var duration = TimeSpan.FromMilliseconds(280);
        item.BeginAnimation(UIElement.OpacityProperty, Delayed(0, 1, delay, duration));
        translate.BeginAnimation(TranslateTransform.YProperty, Delayed(12, 0, delay, duration));
    }

    /// <summary>Анимация с задержкой, которая держит начальное значение всю задержку (без мигания).</summary>
    private static DoubleAnimationUsingKeyFrames Delayed(double from, double to, TimeSpan delay, TimeSpan duration)
    {
        var a = new DoubleAnimationUsingKeyFrames();
        a.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        if (delay > TimeSpan.Zero) a.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(delay)));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(delay + duration), EaseOut));
        return a;
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
