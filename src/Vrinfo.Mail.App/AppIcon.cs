using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Vrinfo.Mail.App;

internal static class AppIcon
{
    public static string PngPath
        => Path.Combine(AppContext.BaseDirectory, "Assets", "app.png");

    public static string IcoPath
        => Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");

    public static string FilePath => File.Exists(IcoPath) ? IcoPath : PngPath;

    public static void ApplyTo(Window window)
    {
        var path = FilePath;
        if (!File.Exists(path))
            return;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        window.Icon = image;
    }
}
