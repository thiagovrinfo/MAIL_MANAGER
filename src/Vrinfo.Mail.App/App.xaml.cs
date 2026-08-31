using Vrinfo.Mail.Imap;

namespace Vrinfo.Mail.App;

public partial class App : System.Windows.Application
{
    private TrayController? _tray;
    public static ShellViewModel Shell { get; private set; } = null!;

    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        SingleInstance.ReplaceRunning();
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            System.Windows.MessageBox.Show(args.Exception.Message, "VRINFO Mail");
        };

        Shell = new ShellViewModel();
        RamGuard.EnableProcessLimits();
        DesktopShortcut.Ensure();
        _tray = new TrayController(this, Shell);
        Shell.ToastRequested += (title, body) =>
        {
            if (Shell.EnableTray)
                PushToastWindow.Enqueue(title, body);
        };
        var startMinimized = e.Args.Any(a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase));

        var store = new MailSettingsStore();
        Theme.Apply(store.Load().UseDarkTheme);
        if (!store.HasConfiguredAccount())
        {
            var setup = new SetupWindow { DataContext = new SetupViewModel(store) };
            if (setup.ShowDialog() != true)
            {
                Shutdown();
                return;
            }
        }

        var main = new MainWindow { DataContext = Shell };
        MainWindow = main;
        if (!startMinimized)
            main.Show();
        else
            _tray.ShowBalloon(Vrinfo.Mail.Core.MailConstants.ProductName, "Rodando na bandeja.");

        await Shell.StartAsync();
        ApplyTray(Shell.EnableTray);
    }

    public void ApplyTray(bool enabled)
    {
        _tray?.SetVisible(enabled);
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        Shell?.Dispose();
        _tray?.Dispose();
        base.OnExit(e);
    }
}
