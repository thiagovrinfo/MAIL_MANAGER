using System.IO;
using Vrinfo.Mail.Core;

namespace Vrinfo.Mail.App;

internal static class SignatureStore
{
    public static string? ResolvePath()
    {
        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VRINFO.Mail");
        var cache = Path.Combine(cacheDir, "AssinaturaThiago.png");
        Directory.CreateDirectory(cacheDir);

        try
        {
            if (File.Exists(MailConstants.SignatureUncPath))
            {
                File.Copy(MailConstants.SignatureUncPath, cache, true);
                return cache;
            }
        }
        catch
        {
            // usa cache ou o arquivo empacotado
        }

        if (File.Exists(cache))
            return cache;

        var bundled = Path.Combine(AppContext.BaseDirectory, "Assets", "AssinaturaThiago.png");
        return File.Exists(bundled) ? bundled : null;
    }
}
