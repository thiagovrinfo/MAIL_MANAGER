using System.Windows;

namespace Vrinfo.Mail.App;

public partial class SettingsWindow : System.Windows.Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        AppIcon.ApplyTo(this);
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.Password = PasswordBox.Password;
            if (vm.SaveCommand.CanExecute(this))
                await vm.SaveCommand.ExecuteAsync(this);
        }
    }
}
