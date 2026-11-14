using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SimsModManager.Core;

namespace SimsModManager.App.ViewModels;

/// <summary>
/// Скриншот окна для сайта и соцсетей: Ctrl+Shift+F12. Окно рисуется заново в 3 раза крупнее
/// (интерфейс векторный, поэтому текст и значки получаются чёткими), имя пользователя в пути скрыто.
/// </summary>
public sealed partial class MainViewModel
{
    public const int ScreenshotScale = 3;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SimsFolderShown))]
    private bool _screenshotMode;

    partial void OnSimsFolderChanged(string? value) => OnPropertyChanged(nameof(SimsFolderShown));

    /// <summary>Путь к папке игры для показа; на скриншоте вместо имени пользователя — «Simmer».</summary>
    public string? SimsFolderShown => ScreenshotMode && SimsFolder is { } f
        ? Regex.Replace(f, @"(?i)(\\Users\\)[^\\]+", "$1Simmer")
        : SimsFolder;

    public async Task SaveScreenshotAsync(FrameworkElement root)
    {
        try
        {
            ScreenshotMode = true;
            // даём разметке обновиться (путь без имени пользователя) перед снимком
            await root.Dispatcher.InvokeAsync(() => root.UpdateLayout(), DispatcherPriority.Render);

            var width = (int)Math.Ceiling(root.ActualWidth * ScreenshotScale);
            var height = (int)Math.Ceiling(root.ActualHeight * ScreenshotScale);
            var bitmap = new RenderTargetBitmap(width, height, 96 * ScreenshotScale, 96 * ScreenshotScale, PixelFormats.Pbgra32);
            // Фон окна рисует Windows (Mica, размытие «Стекла»), а не сам интерфейс — без подложки на снимке
            // вместо него была бы прозрачность. Подкладываем непрозрачный цвет окна текущей темы.
            var backdrop = new DrawingVisual();
            using (var dc = backdrop.RenderOpen())
                dc.DrawRectangle(new SolidColorBrush(BackdropColor(root)), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            bitmap.Render(backdrop);
            bitmap.Render(root);

            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), Core.AppIdentity.Name);
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            await using (var stream = File.Create(file))
                encoder.Save(stream);

            ShowToast(L.T("Скриншот сохранён в «Изображения»"), Icons.Check);
            try { Process.Start("explorer.exe", $"/select,\"{file}\""); } catch (Exception) { }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OutOfMemoryException)
        {
            ShowToast(L.T("Не получилось сохранить скриншот: ") + ex.Message, Icons.Alert);
        }
        finally
        {
            ScreenshotMode = false;
        }
    }

    /// <summary>Цвет окна темы, без прозрачности: у «Стекла» он полупрозрачный — смешиваем с графитом.</summary>
    private static Color BackdropColor(FrameworkElement root)
    {
        var window = root.TryFindResource("B.Window") is SolidColorBrush b ? b.Color : Color.FromRgb(0x0F, 0x10, 0x12);
        if (window.A == 255) return window;
        var under = Color.FromRgb(0x1C, 0x1D, 0x21);
        double a = window.A / 255.0;
        byte Mix(byte top, byte bottom) => (byte)Math.Round(top * a + bottom * (1 - a));
        return Color.FromRgb(Mix(window.R, under.R), Mix(window.G, under.G), Mix(window.B, under.B));
    }
}
