using System.Windows;

namespace Vrinfo.Mail.App;

public partial class SetupWindow : System.Windows.Window
{
    public SetupWindow()
    {
        InitializeComponent();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (DataContext is SetupViewModel vm)
        {
            vm.Password = PasswordBox.Password;
            vm.SaveCommand.Execute(this);
        }
    }
}
