using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace SimsModManager.App.Services;

/// <summary>
/// Темы оформления. Палитра — первый словарь ресурсов приложения; все цвета в разметке
/// подключены через DynamicResource, поэтому замена словаря сразу перекрашивает окно.
/// Тема «Стекло» включает системное размытие Windows 11 (Acrylic) за окном.
/// </summary>
public static class ThemeService
{
    public const string Dark = "Dark";
    public const string Light = "Light";
    public const string Glass = "Glass";

    public static string Current { get; private set; } = Dark;

    /// <summary>Размытие фона за окном есть в Windows 11 22H2 и новее.</summary>
    public static bool GlassSupported => Environment.OSVersion.Version.Build >= 22621;

    public static string Normalize(string? name) => name switch
    {
        Light => Light,
        Glass when GlassSupported => Glass,
        _ => Dark,
    };

    public static void Apply(string? name)
    {
        var theme = Normalize(name);
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var palette = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Themes/Palettes/{theme}.xaml") };
        if (dictionaries.Count > 0) dictionaries[0] = palette;
        else dictionaries.Add(palette);
        Current = theme;

        foreach (Window window in Application.Current.Windows) ApplyWindow(window);
    }

    // ---------- Системная рамка и размытие ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private const int GwlStyle = -16;
    private const long WsSysMenu = 0x00080000;
    private const uint SwpFrameChanged = 0x0020, SwpNoMove = 0x0002, SwpNoSize = 0x0001, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;

    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmBorderColor = 34;
    private const int DwmSystemBackdropType = 38;

    public static void ApplyWindow(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var glass = Current == Glass;

        // Рамка окна: при «Стекле» стекло растягивается на всё окно, иначе его нет.
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 28,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = glass ? new Thickness(-1) : new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });

        if (hwnd == IntPtr.Zero) return; // окно ещё не создано — вызовем снова из SourceInitialized

        try
        {
            // Когда стекло растянуто на всё окно, Windows рисует поверх свои кнопки
            // «свернуть/развернуть/закрыть» — получаются двойные. Без WS_SYSMENU она их не рисует,
            // а наши кнопки в заголовке работают как обычно.
            var style = (long)GetWindowLongPtr(hwnd, GwlStyle);
            var newStyle = glass ? style & ~WsSysMenu : style | WsSysMenu;
            if (newStyle != style)
            {
                SetWindowLongPtr(hwnd, GwlStyle, (IntPtr)newStyle);
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpFrameChanged | SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate);
            }

            if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target })
                target.BackgroundColor = glass ? Colors.Transparent : Colors.Black;

            var dark = Current == Light ? 0 : 1;
            DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref dark, sizeof(int));

            var round = 2;
            DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref round, sizeof(int));

            // Цвет тонкой рамки (формат 0x00BBGGRR); у стекла — системный
            var border = Current switch
            {
                Light => 0x00C7D5DC,
                Glass => unchecked((int)0xFFFFFFFF),
                _ => 0x00414141,
            };
            DwmSetWindowAttribute(hwnd, DwmBorderColor, ref border, sizeof(int));

            if (GlassSupported)
            {
                if (glass)
                {
                    var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                    DwmExtendFrameIntoClientArea(hwnd, ref margins);
                }
                var backdrop = glass ? 3 : 1; // 3 — Acrylic (размытие), 1 — без эффекта
                DwmSetWindowAttribute(hwnd, DwmSystemBackdropType, ref backdrop, sizeof(int));
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Старая Windows — без скруглений и размытия, просто сплошной цвет.
        }
    }
}
