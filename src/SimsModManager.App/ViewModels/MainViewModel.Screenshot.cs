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
}
