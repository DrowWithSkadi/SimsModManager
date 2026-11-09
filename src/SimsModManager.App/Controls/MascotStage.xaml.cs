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
    private const double StatusBar = 26;    // высота строки состояния

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
        AppPage.Library or AppPage.InstallResult => 0,
        AppPage.Settings => 2,
        AppPage.Health or AppPage.NotFound => 1,
        _ => 3, // карточка мода, настройка одежды
    };

    /// <summary>Левый верхний угол Коробка для места: ноги стоят на линии интерфейса.</summary>
    private Point PerchPoint(int perch)
    {
        var w = Math.Max(ActualWidth, 400);
        var h = Math.Max(ActualHeight, 300);
        var x = perch switch
        {
            0 => 290,                     // в шапке, справа от названия
            1 => w * 0.52,                // в шапке, посередине
            2 => 200,                     // на строке состояния слева
            _ => Math.Max(300, w - 320),  // на строке состояния справа
        };
        var line = perch is 0 or 1 ? HeaderLine : h - StatusBar;
        return new Point(x, line - BuddyHeight + 1);
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

    private void OnPageChanged()
    {
        if (!IsLoaded) return;
        var target = PerchFor(Page);
        if (target != _perch) LeapTo(target);
    }

    /// <summary>Прыжок дугой на другое место.</summary>
    private void LeapTo(int perch)
    {
        _perch = perch;
        var from = new Point(Canvas.GetLeft(Buddy), Canvas.GetTop(Buddy));
        if (double.IsNaN(from.X)) { PlaceAt(perch); return; }
        var to = PerchPoint(perch);

        var distance = Math.Abs(to.X - from.X) + Math.Abs(to.Y - from.Y);
        var seconds = Math.Clamp(distance / 900, 0.45, 0.9);
        var peak = Math.Min(from.Y, to.Y) - 60;

        _flying = true;
        Alert.Visibility = Visibility.Collapsed;
        var wasOpen = _bubbleOpen;
        if (wasOpen) HideBubbleQuick();

        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        var x = new DoubleAnimation(from.X, to.X, TimeSpan.FromSeconds(seconds)) { EasingFunction = ease };
        var y = new DoubleAnimationUsingKeyFrames();
        y.KeyFrames.Add(new LinearDoubleKeyFrame(from.Y, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        y.KeyFrames.Add(new EasingDoubleKeyFrame(peak, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds * 0.45)),
            new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        y.KeyFrames.Add(new EasingDoubleKeyFrame(to.Y, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds)),
            new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        y.Completed += (_, _) =>
        {
            _flying = false;
            PlaceAt(_perch);
            if (wasOpen && Notice is not null) OpenBubble();
        };

        Buddy.Leap(seconds);
        Buddy.BeginAnimation(Canvas.LeftProperty, x);
        Buddy.BeginAnimation(Canvas.TopProperty, y);
    }

    // ---------- Тычки ----------

    private void OnPoked()
    {
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
            var later = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.9) };
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
