using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SimsModManager.Core.Content;

namespace SimsModManager.App.Services;

/// <summary>
/// Превращает миниатюру из .package в картинку WPF.
/// У одежды прозрачность хранится отдельной маской — накладываем её на цветную картинку.
/// </summary>
public static class ThumbnailFactory
{
    public static ImageSource? Create(ModThumbnail thumb, int maxSize = 160)
    {
        try
        {
            var color = Decode(thumb.Image);
            if (color is null) return null;

            BitmapSource result = color;
            if (thumb.AlphaMask is not null)
            {
                var alpha = Decode(thumb.AlphaMask);
                if (alpha is not null && alpha.PixelWidth == color.PixelWidth && alpha.PixelHeight == color.PixelHeight)
                    result = ApplyAlpha(color, alpha);
            }

            var scale = Math.Min(1.0, (double)maxSize / Math.Max(result.PixelWidth, result.PixelHeight));
            if (scale < 1)
            {
                result = new TransformedBitmap(result, new ScaleTransform(scale, scale));
            }
            result.Freeze();
            return result;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    private static BitmapSource? Decode(byte[] data)
    {
        using var ms = new MemoryStream(data);
        var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0) return null;
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }

    private static BitmapSource ApplyAlpha(BitmapSource color, BitmapSource alpha)
    {
        int w = color.PixelWidth, h = color.PixelHeight;
        var bgra = new FormatConvertedBitmap(color, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[w * h * 4];
        bgra.CopyPixels(pixels, w * 4, 0);

        var gray = new FormatConvertedBitmap(alpha, PixelFormats.Gray8, null, 0);
        var mask = new byte[w * h];
        gray.CopyPixels(mask, w, 0);

        for (var i = 0; i < mask.Length; i++) pixels[i * 4 + 3] = mask[i];

        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
        bmp.Freeze();
        return bmp;
    }
}
