using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SimsModManager.App.ViewModels;

namespace SimsModManager.App.Controls;

/// <summary>
/// Где живёт Коробок. Слой лежит поверх шапки, содержимого и строки состояния.
/// У каждой страницы своё «место» (насест) — при переходе Коробок туда перепрыгивает.
/// Если по нему настучать (3 тычка подряд), у него кружится голова и он убегает на другое место.
/// Предупреждения библиотеки он показывает в белом облачке с кнопками «исправить».
/// </summary>
public partial class MascotStage : UserControl
{
    private const double BuddyWidth = 62;
    private const double BuddyHeight = 32;
    private const double HeaderLine = 72;   // нижняя линия шапки
    private const double StatusBar = 40;    // от низа окна до верхнего края строки состояния (28 + отступ 12)

    private static readonly Brush WarningBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xA8, 0x38));
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x56, 0x4F));

    private readonly Random _random = new();
    private readonly List<DateTime> _pokes = [];
    private readonly DispatcherTimer _infoTimer = new() { Interval = TimeSpan.FromSeconds(7) };
    private int _perch;
    private bool _flying;
    private bool _bubbleOpen;

    public MascotStage()
    {
        InitializeComponent();
        Buddy.Width = BuddyWidth;
        Buddy.Height = BuddyHeight;
        Buddy.Poked += OnPoked;
        Loaded += (_, _) =>
        {
            _perch = PerchFor(Page);
            PlaceAt(_perch);
            if (Notice is not null) ShowNotice(Notice, announce: false);
        };
        SizeChanged += (_, _) =>
        {
            if (!_flying) PlaceAt(_perch);
        };
        LayoutUpdated += (_, _) =>
        {
            // Шапка поменялась (другой путь, появились кнопки) — тихо пересаживаемся
            if (_flying || !IsLoaded || _perch is not (0 or 4) || _pendingLeap?.IsEnabled == true) return;
            var p = PerchPoint(_perch);
            if (Math.Abs(p.X - Canvas.GetLeft(Buddy)) > 1 || Math.Abs(p.Y - Canvas.GetTop(Buddy)) > 1) PlaceAt(_perch);
        };
        _infoTimer.Tick += (_, _) =>
        {
            _infoTimer.Stop();
            if (Notice?.Kind == NoticeKind.Info) CloseBubble();
        };
    }

    // ---------- Свойства ----------

    public static readonly DependencyProperty PageProperty = DependencyProperty.Register(
        nameof(Page), typeof(AppPage), typeof(MascotStage), new PropertyMetadata(AppPage.Library, (d, _) => ((MascotStage)d).OnPageChanged()));

    public AppPage Page { get => (AppPage)GetValue(PageProperty); set => SetValue(PageProperty, value); }

    public static readonly DependencyProperty NoticeProperty = DependencyProperty.Register(
        nameof(Notice), typeof(MascotNotice), typeof(MascotStage), new PropertyMetadata(null, (d, e) => ((MascotStage)d).OnNoticeChanged((MascotNotice?)e.OldValue, (MascotNotice?)e.NewValue)));

    public MascotNotice? Notice { get => (MascotNotice?)GetValue(NoticeProperty); set => SetValue(NoticeProperty, value); }

    // ---------- Места ----------

    /// <summary>У каждой страницы — своё место, чтобы Коробок «ходил» за тобой по вкладкам.</summary>
    private static int PerchFor(AppPage page) => page switch
    {
        AppPage.Library or AppPage.InstallResult => 0, // в шапке
        AppPage.Settings => 2,
        AppPage.Health or AppPage.NotFound => 1,
        _ => 3, // карточка мода, настройка одежды
    };

    /// <summary>
    /// Левый верхний угол Коробка для места: ноги стоят на линии интерфейса.
    /// В шапке он садится в свободный промежуток между названием (с путём к папке) и кнопками, чтобы ничего не закрывать.
    /// </summary>
    private Point PerchPoint(int perch)
    {
        var w = Math.Max(ActualWidth, 400);
        var h = Math.Max(ActualHeight, 300);
        if (perch == 4 && PlayButtonTop() is { } onButton) return onButton;
        if (perch is 0 or 4) return new Point(HeaderGapX(w), HeaderLine - BuddyHeight + 1);
        var x = perch switch
        {
            1 => w / 2 - BuddyWidth / 2,  // на строке состояния посередине
            2 => 200,                     // на строке состояния слева
            _ => Math.Max(300, w - 320),  // на строке состояния справа
        };
        return new Point(x, h - StatusBar - BuddyHeight + 1);
    }

    /// <summary>Место на верхнем краю кнопки «Играть» (ближе к правому краю), если она видна.</summary>
    private Point? PlayButtonTop()
    {
        if (Window.GetWindow(this)?.FindName("PlayButton") is not FrameworkElement button || !button.IsVisible || button.Opacity < 0.9) return null;
        var topLeft = button.TranslatePoint(new Point(0, 0), this);
        return new Point(topLeft.X + button.ActualWidth - BuddyWidth - 22, topLeft.Y - BuddyHeight + 4);
    }

    /// <summary>Середина пустого места в шапке между текстом слева и кнопками справа.</summary>
    private double HeaderGapX(double width)
    {
        var window = Window.GetWindow(this);
        var left = 300.0;
        var right = width - 24;
        if (window?.FindName("HeaderTexts") is FrameworkElement texts && texts.IsVisible)
            left = texts.TranslatePoint(new Point(texts.ActualWidth, 0), this).X + 16;
        if (window?.FindName("HeaderButtons") is FrameworkElement buttons && buttons.IsVisible)
            right = buttons.TranslatePoint(new Point(0, 0), this).X - 16;
        // Места мало — встаём вплотную к кнопкам
        if (right - left < BuddyWidth) return Math.Max(left, right - BuddyWidth);
        return (left + right) / 2 - BuddyWidth / 2;
    }

    private void PlaceAt(int perch)
    {
        var p = PerchPoint(perch);
        Buddy.BeginAnimation(Canvas.LeftProperty, null);
        Buddy.BeginAnimation(Canvas.TopProperty, null);
        Canvas.SetLeft(Buddy, p.X);
        Canvas.SetTop(Buddy, p.Y);
        PlaceExtras();
    }

    private DispatcherTimer? _pendingLeap;

    private void OnPageChanged()
    {
        if (!IsLoaded) return;
        var target = PerchFor(Page);
        _pendingLeap?.Stop();
        if (target == _perch) return;
        if (target != 4)
        {
            LeapTo(target);
            return;
        }
        // Кнопка «Играть» только начинает появляться — прыгаем, когда она на месте (до 0.6 с)
        var started = DateTime.Now;
        _pendingLeap = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _pendingLeap.Tick += (_, _) =>
        {
            var ready = PlayButtonTop() is not null;
            if (!ready && DateTime.Now - started < TimeSpan.FromMilliseconds(600)) return;
            _pendingLeap!.Stop();
            if (PerchFor(Page) == 4) LeapTo(4);
        };
        _pendingLeap.Start();
    }

    /// <summary>
    /// Прыжок на другое место: сначала приседает, потом летит по настоящей параболе
    /// (вверх быстрее, на вершине замедляется, вниз ускоряется), в конце сплющивается.
    /// </summary>
    private void LeapTo(int perch)
    {
        _perch = perch;
        var from = new Point(Canvas.GetLeft(Buddy), Canvas.GetTop(Buddy));
        if (double.IsNaN(from.X)) { PlaceAt(perch); return; }
        var to = PerchPoint(perch);

        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < 4) { PlaceAt(perch); return; }

        // Чем дальше — тем дольше и выше полёт
        var flight = Math.Clamp(0.5 + distance / 1400, 0.6, 1.25);
        var crouch = 0.18;
        var apex = Math.Min(from.Y, to.Y) - Math.Clamp(40 + distance * 0.08, 50, 110);
        apex = Math.Max(apex, -24); // не улетаем далеко за верх окна

        // Парабола через три точки: старт, вершина, финиш. Вершина ближе к той точке, что выше.
        var a0 = from.Y - apex;
        var a1 = to.Y - apex;
        var tApex = Math.Sqrt(a0) / (Math.Sqrt(a0) + Math.Sqrt(a1));

        _flying = true;
        Alert.Visibility = Visibility.Collapsed;
        var wasOpen = _bubbleOpen;
        if (wasOpen) HideBubbleQuick();

        var x = new DoubleAnimationUsingKeyFrames();
        var y = new DoubleAnimationUsingKeyFrames();
        x.KeyFrames.Add(new DiscreteDoubleKeyFrame(from.X, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        y.KeyFrames.Add(new DiscreteDoubleKeyFrame(from.Y, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        x.KeyFrames.Add(new DiscreteDoubleKeyFrame(from.X, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(crouch))));
        y.KeyFrames.Add(new DiscreteDoubleKeyFrame(from.Y, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(crouch))));
        const int steps = 36;
        for (var i = 1; i <= steps; i++)
        {
            var t = (double)i / steps;
            var time = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(crouch + flight * t));
            x.KeyFrames.Add(new LinearDoubleKeyFrame(from.X + dx * t, time));
            // Высота: до вершины — подъём (замедляясь), после — падение (ускоряясь)
            double yy;
            if (t <= tApex)
            {
                var u = t / tApex;
                yy = from.Y - a0 * (1 - (1 - u) * (1 - u));
            }
            else
            {
                var u = (t - tApex) / (1 - tApex);
                yy = apex + a1 * u * u;
            }
            y.KeyFrames.Add(new LinearDoubleKeyFrame(yy, time));
        }
        y.Completed += (_, _) =>
        {
            _flying = false;
            PlaceAt(_perch);
            if (wasOpen && Notice is not null) OpenBubble();
        };

        Buddy.Leap(crouch, flight, Math.Sign(dx));
        Buddy.BeginAnimation(Canvas.LeftProperty, x);
        Buddy.BeginAnimation(Canvas.TopProperty, y);
    }

    // ---------- Тычки ----------

    private void OnPoked()
    {
        if (_flying) return;
        var now = DateTime.Now;
        _pokes.Add(now);
        _pokes.RemoveAll(t => now - t > TimeSpan.FromSeconds(1.5));

        if (_pokes.Count >= 3 && !_flying)
        {
            _pokes.Clear();
            Buddy.Dizzy();
            // Отбегает на любое другое место
            var others = Enumerable.Range(0, 4).Where(p => p != _perch).ToList();
            var target = others[_random.Next(others.Count)];
            var later = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.0) };
            later.Tick += (_, _) =>
            {
                later.Stop();
                LeapTo(target);
            };
            later.Start();
            return;
        }

        // Одиночный тычок — открыть/закрыть облачко, если есть что сказать
        if (_pokes.Count == 1 && Notice is not null)
        {
            if (_bubbleOpen) CloseBubble();
            else OpenBubble();
        }
    }

    // ---------- Облачко ----------

    private void OnNoticeChanged(MascotNotice? old, MascotNotice? notice)
    {
        if (!IsLoaded) return;
        if (notice is null)
        {
            CloseBubble();
            Alert.Visibility = Visibility.Collapsed;
            return;
        }
        // Новое сообщение — показываем; обновился только текст того же — тихо обновляем
        if (old?.Key != notice.Key) ShowNotice(notice, announce: true);
        else
        {
            Bubble.DataContext = notice;
            if (_bubbleOpen) PlaceExtras();
        }
    }

    private void ShowNotice(MascotNotice notice, bool announce)
    {
        Bubble.DataContext = notice;
        var brush = notice.Kind == NoticeKind.Error ? ErrorBrush : WarningBrush;
        Badge.Background = brush;
        Badge.Visibility = notice.IsAlert ? Visibility.Visible : Visibility.Collapsed;
        Alert.Background = notice.IsAlert ? brush : new SolidColorBrush(Color.FromRgb(0x77, 0xE1, 0xC7));
        if (announce) Buddy.Announce();
        OpenBubble();
        if (notice.Kind == NoticeKind.Info) _infoTimer.Start();
    }

    private void OpenBubble()
    {
        if (_flying) return;
        _bubbleOpen = true;
        Alert.Visibility = Visibility.Collapsed;
        Bubble.Visibility = Visibility.Visible;
        PlaceExtras();
        var pop = new DoubleAnimation(0.85, 1, TimeSpan.FromSeconds(0.25)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 } };
        BubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        BubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        Bubble.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.15)));
    }

    private void CloseBubble()
    {
        _infoTimer.Stop();
        if (!_bubbleOpen) return;
        _bubbleOpen = false;
        var fade = new DoubleAnimation(0, TimeSpan.FromSeconds(0.15));
        fade.Completed += (_, _) =>
        {
            if (!_bubbleOpen) Bubble.Visibility = Visibility.Collapsed;
        };
        Bubble.BeginAnimation(OpacityProperty, fade);
        // Предупреждение не решено — над головой остаётся «!»
        if (Notice?.IsAlert == true)
        {
            Alert.Visibility = Visibility.Visible;
            PlaceExtras();
        }
    }

    private void HideBubbleQuick()
    {
        _bubbleOpen = false;
        Bubble.BeginAnimation(OpacityProperty, null);
        Bubble.Visibility = Visibility.Collapsed;
    }

    private void OnCloseBubble(object sender, RoutedEventArgs e) => CloseBubble();

    private void OnBubbleAction(object sender, RoutedEventArgs e)
    {
        // Кнопка нажата — облачко прячем; если проблема останется, «!» напомнит
        Dispatcher.InvokeAsync(CloseBubble, DispatcherPriority.Background);
    }

    /// <summary>Ставит облачко и «!» рядом с Коробком, не вылезая за края окна.</summary>
    private void PlaceExtras()
    {
        var left = Canvas.GetLeft(Buddy);
        var top = Canvas.GetTop(Buddy);
        if (double.IsNaN(left)) return;

        Canvas.SetLeft(Alert, left + BuddyWidth - 14);
        Canvas.SetTop(Alert, top - 16);

        if (Bubble.Visibility != Visibility.Visible) return;
        Bubble.Measure(new Size(Bubble.Width, double.PositiveInfinity));
        var size = Bubble.DesiredSize;
        var centerX = left + BuddyWidth / 2;
        var bx = Math.Clamp(centerX - size.Width / 2, 12, Math.Max(12, ActualWidth - size.Width - 12));

        // Коробок наверху — облачко под ним, внизу — над ним
        var below = top < ActualHeight / 2;
        var by = below ? top + BuddyHeight + 2 : top - size.Height - 2;
        Canvas.SetLeft(Bubble, bx);
        Canvas.SetTop(Bubble, by);
        TailTop.Visibility = below ? Visibility.Visible : Visibility.Collapsed;
        TailBottom.Visibility = below ? Visibility.Collapsed : Visibility.Visible;
        var tailX = Math.Clamp(centerX - bx - 9, 16, size.Width - 34);
        TailTop.Margin = TailBottom.Margin = new Thickness(tailX, 0, 0, 0);
        Bubble.RenderTransformOrigin = new Point((tailX + 9) / size.Width, below ? 0 : 1);
        // «!» сидит на левом верхнем углу облачка
        Badge.Margin = new Thickness(-7, below ? 6 : 0, 0, 0);
    }
}
