using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace Vrinfo.Mail.App;

public partial class MainWindow : System.Windows.Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Opacity = 0;
        AppIcon.ApplyTo(this);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        });
        if (DataContext is ShellViewModel vm)
        {
            vm.HtmlChanged += html =>
            {
                _ = Dispatcher.InvokeAsync(() => ShowHtml(html), System.Windows.Threading.DispatcherPriority.Background);
            };
            Theme.Changed += () =>
            {
                _ = Dispatcher.InvokeAsync(() =>
                {
                    if (DataContext is ShellViewModel shell)
                        ShowHtml(shell.HtmlBody);
                });
            };
        }

        var userData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VRINFO.Mail",
            "webview");
        Directory.CreateDirectory(userData);
        var options = new CoreWebView2EnvironmentOptions(
            "--disable-extensions --disable-background-networking --disable-sync --disable-gpu-vsync --js-flags=--max-old-space-size=64");
        var env = await CoreWebView2Environment.CreateAsync(null, userData, options);
        await Reader.EnsureCoreWebView2Async(env);
        Reader.DefaultBackgroundColor = Theme.ReaderBackColor();
        if (DataContext is ShellViewModel ready)
            ShowHtml(ready.HtmlBody);
        Reader.CoreWebView2.Settings.IsScriptEnabled = false;
        Reader.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        Reader.CoreWebView2.Settings.AreDevToolsEnabled = false;
        Reader.CoreWebView2.Settings.IsStatusBarEnabled = false;
        Reader.CoreWebView2.Settings.AreHostObjectsAllowed = false;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        if (Reader.CoreWebView2 is not null)
            Reader.NavigateToString("<html></html>");
        Hide();
        RamGuard.Trim();
    }

    private void ShowHtml(string? html)
    {
        if (Reader.CoreWebView2 is null)
            return;
        Reader.DefaultBackgroundColor = Theme.ReaderBackColor();
        Reader.NavigateToString(MailHtml.Wrap(html, Theme.IsDark));
    }
}
