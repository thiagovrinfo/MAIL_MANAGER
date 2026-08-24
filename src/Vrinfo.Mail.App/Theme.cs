using System.Windows;
using System.Windows.Media;

namespace Vrinfo.Mail.App;

internal static class Theme
{
    public static bool IsDark { get; private set; }

    public static event Action? Changed;

    public static void Apply(bool dark)
    {
        var app = System.Windows.Application.Current;
        if (app is null)
            return;

        IsDark = dark;
        var r = app.Resources;

        if (dark)
        {
            Set(r, "Bg", 14, 20, 28);
            Set(r, "Panel", 23, 31, 42);
            Set(r, "Blue", 21, 101, 192);
            Set(r, "Text", 241, 245, 249);
            Set(r, "Muted", 180, 194, 208);
            Set(r, "Line", 58, 74, 92);
            Set(r, "HeaderFg", 255, 255, 255);
            Set(r, "HeaderSub", 197, 212, 232);
            Set(r, "ListSelected", 42, 63, 88);
            Set(r, "ListHover", 34, 48, 64);
            Set(r, "StatusBg", 18, 26, 36);
            Set(r, "ReaderBg", 18, 24, 32);
            Set(r, "InputBg", 28, 38, 51);
            Set(r, "GhostBg", 42, 54, 70);
            Set(r, "GhostFg", 144, 202, 249);
            Set(r, "ChipBg", 42, 63, 88);
            Set(r, "IconPlate", 197, 205, 216);
            Set(r, "OverlayScrim", 180, 11, 16, 24);
            Set(r, "CardBg", 28, 38, 51);
            Set(r, "ProgressTrack", 42, 54, 70);
            Set(r, "SignatureBg", 28, 38, 51);
            Set(r, "HeaderGhostBg", 51, 255, 255, 255);
            Set(r, "HeaderGhostFg", 255, 255, 255);
            Set(r, "Danger", 239, 154, 154);
        }
        else
        {
            Set(r, "Bg", 244, 247, 251);
            Set(r, "Panel", 255, 255, 255);
            Set(r, "Blue", 21, 101, 192);
            Set(r, "Text", 13, 27, 42);
            Set(r, "Muted", 61, 82, 99);
            Set(r, "Line", 144, 202, 249);
            Set(r, "HeaderFg", 255, 255, 255);
            Set(r, "HeaderSub", 187, 222, 251);
            Set(r, "ListSelected", 187, 222, 251);
            Set(r, "ListHover", 227, 242, 253);
            Set(r, "StatusBg", 232, 238, 245);
            Set(r, "ReaderBg", 255, 255, 255);
            Set(r, "InputBg", 255, 255, 255);
            Set(r, "GhostBg", 227, 232, 238);
            Set(r, "GhostFg", 13, 71, 161);
            Set(r, "ChipBg", 187, 222, 251);
            Set(r, "IconPlate", 30, 136, 229);
            Set(r, "OverlayScrim", 153, 244, 247, 251);
            Set(r, "CardBg", 255, 255, 255);
            Set(r, "ProgressTrack", 187, 222, 251);
            Set(r, "SignatureBg", 240, 244, 248);
            Set(r, "HeaderGhostBg", 51, 255, 255, 255);
            Set(r, "HeaderGhostFg", 255, 255, 255);
            Set(r, "Danger", 198, 40, 40);
        }

        Changed?.Invoke();
    }

    public static System.Drawing.Color ReaderBackColor() =>
        IsDark
            ? System.Drawing.Color.FromArgb(255, 18, 24, 32)
            : System.Drawing.Color.FromArgb(255, 255, 255, 255);

    private static void Set(ResourceDictionary r, string key, byte a, byte red, byte green, byte blue)
        => SetBrush(r, key, System.Windows.Media.Color.FromArgb(a, red, green, blue));

    private static void Set(ResourceDictionary r, string key, byte red, byte green, byte blue)
        => SetBrush(r, key, System.Windows.Media.Color.FromRgb(red, green, blue));

    private static void SetBrush(ResourceDictionary r, string key, System.Windows.Media.Color color)
    {
        if (r[key] is SolidColorBrush existing && !existing.IsFrozen)
        {
            existing.Color = color;
            return;
        }

        r[key] = new SolidColorBrush(color);
    }
}
