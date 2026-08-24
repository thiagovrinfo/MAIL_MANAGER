using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;
using Vrinfo.Mail.Core;

namespace Vrinfo.Mail.App;

public sealed class TrayController : IDisposable
{
    private readonly App _app;
    private readonly ShellViewModel _shell;
    private readonly Forms.NotifyIcon _icon;
    private readonly System.Drawing.Icon _trayIcon;

    public TrayController(App app, ShellViewModel shell)
    {
        _app = app;
        _shell = shell;
        _trayIcon = LoadAppIcon();
        _icon = new Forms.NotifyIcon
        {
            Text = MailConstants.ProductName,
            Visible = true,
            Icon = _trayIcon
        };
        _icon.DoubleClick += (_, _) => ShowMain();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir", null, (_, _) => ShowMain());
        menu.Items.Add("Sair", null, (_, _) =>
        {
            _icon.Visible = false;
            _app.Shutdown();
        });
        _icon.ContextMenuStrip = menu;
    }

    public void ShowBalloon(string title, string text)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = string.IsNullOrWhiteSpace(text) ? MailConstants.ProductName : text;
        _icon.ShowBalloonTip(4000);
    }

    private void ShowMain()
    {
        if (_app.MainWindow is null)
            return;
        _app.MainWindow.Show();
        _app.MainWindow.WindowState = System.Windows.WindowState.Normal;
        _app.MainWindow.Activate();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _trayIcon.Dispose();
    }

    private static System.Drawing.Icon LoadAppIcon()
    {
        var ico = AppIcon.IcoPath;
        if (File.Exists(ico))
            return new System.Drawing.Icon(ico);

        var png = AppIcon.PngPath;
        if (File.Exists(png))
        {
            using var bitmap = new System.Drawing.Bitmap(png);
            var handle = bitmap.GetHicon();
            using var created = System.Drawing.Icon.FromHandle(handle);
            var clone = (System.Drawing.Icon)created.Clone();
            _ = NativeMethods.DestroyIcon(handle);
            return clone;
        }

        return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);
    }
}
