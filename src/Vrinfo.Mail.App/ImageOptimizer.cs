using System.IO;
using System.Windows.Media.Imaging;

namespace Vrinfo.Mail.App;

internal static class ImageOptimizer
{
    public static string Optimize(string sourcePath, int maxWidth, int jpegQuality)
    {
        using var input = File.OpenRead(sourcePath);
        var decoder = BitmapDecoder.Create(
            input,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];

        BitmapSource source = frame;
        if (frame.PixelWidth > maxWidth && maxWidth > 40)
        {
            var scale = maxWidth / (double)frame.PixelWidth;
            source = new TransformedBitmap(frame, new System.Windows.Media.ScaleTransform(scale, scale));
        }

        var encoder = new JpegBitmapEncoder { QualityLevel = Math.Clamp(jpegQuality, 40, 100) };
        encoder.Frames.Add(BitmapFrame.Create(source));
        var dest = Path.Combine(
            Path.GetTempPath(),
            "vrinfo-mail-" + Guid.NewGuid().ToString("N") + ".jpg");
        using var stream = File.Create(dest);
        encoder.Save(stream);
        return dest;
    }
}
