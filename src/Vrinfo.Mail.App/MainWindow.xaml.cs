using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Vrinfo.Mail.Core;

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
                _ = Dispatcher.InvokeAsync(() => ShowHtml(html), System.Windows.Threading.DispatcherPriority.Send);
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
            ShowHtml(_pendingHtml ?? ready.HtmlBody);
        Reader.CoreWebView2.Settings.IsScriptEnabled = false;
        Reader.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        Reader.CoreWebView2.Settings.AreDevToolsEnabled = false;
        Reader.CoreWebView2.Settings.IsStatusBarEnabled = false;
        Reader.CoreWebView2.Settings.AreHostObjectsAllowed = false;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        if (DataContext is ShellViewModel { EnableTray: true, MinimizeToTray: true })
        {
            if (Reader.CoreWebView2 is not null)
                Reader.NavigateToString("<html></html>");
            Hide();
            RamGuard.Trim();
            return;
        }

        if (System.Windows.Application.Current is App app)
            app.Shutdown();
    }

    private string? _pendingHtml;

    private void OnMessageItemMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;
        if (sender is ListBoxItem { DataContext: IndexedMessage message } &&
            DataContext is ShellViewModel vm)
        {
            vm.OpenFromList(message);
        }
    }

    private void OnMessageItemRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem { DataContext: IndexedMessage message } item || DataContext is not ShellViewModel vm)
            return;

        vm.SelectMessageForContext(message);
        item.IsSelected = true;
        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = item };
        menu.Items.Add(CreateMenuItem("✓  Marcar como lido", OnMarkMessageRead));
        menu.Items.Add(CreateMenuItem("●  Marcar como não lido", OnMarkMessageUnread));
        menu.Items.Add(new Separator());
        var move = new System.Windows.Controls.MenuItem { Header = "Mover sempre este remetente para…" };
        foreach (var folder in vm.SenderRuleFolders)
        {
            var destination = CreateMenuItem(folder.Title, OnCreateSenderFolderRule);
            destination.Tag = folder.Id;
            move.Items.Add(destination);
        }
        menu.Items.Add(move);
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("🗑  Excluir", OnDeleteMessage));
        item.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void OnFolderItemRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem item)
            return;
        item.IsSelected = true;
        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = item };
        menu.Items.Add(CreateMenuItem("✓  Marcar todos como lidos", OnMarkFolderRead));
        item.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private static System.Windows.Controls.MenuItem CreateMenuItem(string header, RoutedEventHandler click)
    {
        var item = new System.Windows.Controls.MenuItem { Header = header };
        item.Click += click;
        return item;
    }

    private async void OnMarkMessageRead(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel vm)
            await vm.SetSelectedSeenAsync(true);
    }

    private async void OnMarkMessageUnread(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel vm)
            await vm.SetSelectedSeenAsync(false);
    }

    private async void OnDeleteMessage(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel vm)
            await vm.DeleteSelectedFromContextAsync();
    }

    private async void OnMarkFolderRead(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel vm)
            await vm.MarkSelectedFolderReadAsync();
    }

    private async void OnCreateSenderFolderRule(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { Tag: string destinationId } || DataContext is not ShellViewModel vm)
            return;
        var destination = vm.SenderRuleFolders.FirstOrDefault(folder =>
            string.Equals(folder.Id, destinationId, StringComparison.OrdinalIgnoreCase));
        if (destination is not null)
            await vm.AddSenderFolderRuleAsync(destination);
    }

    private void ShowHtml(string? html)
    {
        _pendingHtml = html;
        if (Reader.CoreWebView2 is null)
            return;
        Reader.DefaultBackgroundColor = Theme.ReaderBackColor();
        Reader.NavigateToString(MailHtml.Wrap(html, Theme.IsDark));
    }
}
