using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Shell;

namespace SimsModManager.App.Overlay;

public partial class OverlayWindow : Window
{
    public OverlayWindow()
    {
        InitializeComponent();
        // Стекло на всё окно, без системной рамки и заголовка
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = BlurSupported ? new Thickness(-1) : new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            CloseRequested?.Invoke();
        };
        // Alt+F4 тоже просто прячет оверлей — окно живёт всё время работы программы
        Closing += (_, e) =>
        {
            if (_allowClose) return;
            e.Cancel = true;
            CloseRequested?.Invoke();
        };

        var pulse = new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(700))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        BusyDot.BeginAnimation(OpacityProperty, pulse);
    }

    /// <summary>Ширина боковой панели в «логических» пикселях (масштаб экрана учитывается отдельно).</summary>
    public const double PanelWidth = 380;

    private static bool BlurSupported => Environment.OSVersion.Version.Build >= 22621;

    /// <summary>
    /// Полупрозрачный фон с размытием игры позади (Windows 11) — его рисует сама система на видеокарте,
    /// поэтому FPS игры не страдает. На старой Windows — почти непрозрачный тёмный фон.
    /// </summary>
    public void ApplyGlass(IntPtr hwnd)
    {
        try
        {
            var dark = 1;
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            var round = Native.DWMWCP_ROUND;
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            var border = 0x00403D3A; // тонкая рамка (0x00BBGGRR)
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_BORDER_COLOR, ref border, sizeof(int));

            // Без системного меню Windows не дорисовывает свои кнопки поверх стекла
            var style = (long)Native.GetWindowLongPtr(hwnd, Native.GWL_STYLE);
            if ((style & Native.WS_SYSMENU) != 0)
            {
                Native.SetWindowLongPtr(hwnd, Native.GWL_STYLE, (IntPtr)(style & ~Native.WS_SYSMENU));
                Native.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                    Native.SWP_FRAMECHANGED | Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            }

            if (!BlurSupported)
            {
                Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x16, 0x18, 0x17));
                return;
            }
            if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target })
                target.BackgroundColor = Colors.Transparent;
            var margins = new Native.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            Native.DwmExtendFrameIntoClientArea(hwnd, ref margins);
            var acrylic = 3;
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref acrylic, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x16, 0x18, 0x17));
        }
    }

    private bool _allowClose;

    public event Action? CloseRequested;

    public void CloseForReal()
    {
        _allowClose = true;
        Close();
    }

    /// <summary>Короткое появление слева: 160 мс, только прозрачность и сдвиг — это дёшево для видеокарты.</summary>
    public void PlayAppear()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(160);
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        if (Root.RenderTransform is TranslateTransform move)
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-14, 0, duration) { EasingFunction = ease });
        NameBox.Focus();
    }

    private void OnNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not OverlayViewModel vm) return;
        e.Handled = true;
        if (vm.MakeCheckpointCommand.CanExecute(null)) vm.MakeCheckpointCommand.Execute(null);
    }
}

public sealed class InverseVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
