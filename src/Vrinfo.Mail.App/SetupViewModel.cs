using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vrinfo.Mail.Core;
using Vrinfo.Mail.Imap;

namespace Vrinfo.Mail.App;

public sealed partial class SetupViewModel : ObservableObject
{
    private readonly MailSettingsStore _store;

    [ObservableProperty] private string _email = MailConstants.DefaultEmail;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _hint = "Se informar só o usuário, o domínio @vrinfo.com.br é completado.";
    [ObservableProperty] private string? _error;

    public SetupViewModel(MailSettingsStore store)
    {
        _store = store;
        Email = MailConstants.DefaultEmail;
    }

    [RelayCommand]
    private void Save(System.Windows.Window window)
    {
        var email = EmailAddressHelper.CompleteVrinfoAddress(Email);
        if (!EmailAddressHelper.IsValid(email))
        {
            Error = "Informe um e-mail válido (ex.: usuario@vrinfo.com.br).";
            return;
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            Error = "Informe a senha da caixa IMAP.";
            return;
        }

        var settings = _store.Load();
        settings.Email = email;
        settings.DisplayName = "Thiago VRINFO";
        _store.Save(settings);
        WindowsCredentialStore.Save(email, Password);
        window.DialogResult = true;
        window.Close();
    }
}
