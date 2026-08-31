using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vrinfo.Mail.Core;
using Vrinfo.Mail.Imap;

namespace Vrinfo.Mail.App;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly MailSettingsStore _store;
    private readonly MailSettings _original;

    [ObservableProperty] private string email = "";
    [ObservableProperty] private string displayName = "";
    [ObservableProperty] private string alwaysCcText = "";
    [ObservableProperty] private string imapHost = "";
    [ObservableProperty] private string imapPort = "993";
    [ObservableProperty] private string smtpHost = "";
    [ObservableProperty] private string smtpPort = "465";
    [ObservableProperty] private bool startWithWindows = true;
    [ObservableProperty] private bool enableTray = true;
    [ObservableProperty] private bool minimizeToTray = true;
    [ObservableProperty] private bool alwaysConfirmSend;
    [ObservableProperty] private bool alwaysSendHighPriority;
    [ObservableProperty] private bool applyRulesOnSync = true;
    [ObservableProperty] private bool folderInovafarmaEnabled = true;
    [ObservableProperty] private bool folderHiperEnabled = true;
    [ObservableProperty] private bool folderContasEnabled = true;
    [ObservableProperty] private bool folderContabilidadeEnabled = true;
    [ObservableProperty] private bool folderDiscordEnabled = true;
    [ObservableProperty] private string inovafarmaTokens = "";
    [ObservableProperty] private string hiperTokens = "";
    [ObservableProperty] private string contasTokens = "";
    [ObservableProperty] private string contabilidadeTokens = "";
    [ObservableProperty] private string discordTokens = "";
    [ObservableProperty] private string contabilidadeSenders = "";
    [ObservableProperty] private string? error;
    [ObservableProperty] private string status = "";

    public SettingsViewModel(MailSettingsStore store, MailSettings settings, string currentEmail)
    {
        _store = store;
        _original = settings;
        Email = string.IsNullOrWhiteSpace(settings.Email) ? currentEmail : settings.Email;
        DisplayName = settings.DisplayName;
        AlwaysCcText = Join(settings.AlwaysCc);
        ImapHost = settings.ImapHost;
        ImapPort = settings.ImapPort.ToString();
        SmtpHost = settings.SmtpHost;
        SmtpPort = settings.SmtpPort.ToString();
        StartWithWindows = settings.StartWithWindows;
        EnableTray = settings.EnableTray;
        MinimizeToTray = settings.MinimizeToTray;
        AlwaysConfirmSend = settings.AlwaysConfirmSend;
        AlwaysSendHighPriority = settings.AlwaysSendHighPriority;
        ApplyRulesOnSync = settings.ApplyRulesOnSync;
        FolderInovafarmaEnabled = settings.FolderInovafarmaEnabled;
        FolderHiperEnabled = settings.FolderHiperEnabled;
        FolderContasEnabled = settings.FolderContasEnabled;
        FolderContabilidadeEnabled = settings.FolderContabilidadeEnabled;
        FolderDiscordEnabled = settings.FolderDiscordEnabled;
        InovafarmaTokens = Join(settings.InovafarmaTokens);
        HiperTokens = Join(settings.HiperTokens);
        ContasTokens = Join(settings.ContasTokens);
        ContabilidadeTokens = Join(settings.ContabilidadeTokens);
        DiscordTokens = Join(settings.DiscordTokens);
        ContabilidadeSenders = Join(settings.ContabilidadeSenders);
    }

    public string Password { get; set; } = "";

    [RelayCommand]
    private async Task Save(System.Windows.Window window)
    {
        Error = null;
        var email = EmailAddressHelper.CompleteVrinfoAddress(Email);
        if (!EmailAddressHelper.IsValid(email))
        {
            Error = "Informe um e-mail válido.";
            return;
        }

        if (!int.TryParse(ImapPort, out var imapPort) || imapPort is < 1 or > 65535)
        {
            Error = "Porta IMAP inválida.";
            return;
        }

        if (!int.TryParse(SmtpPort, out var smtpPort) || smtpPort is < 1 or > 65535)
        {
            Error = "Porta SMTP inválida.";
            return;
        }

        var settings = _store.Load();
        settings.Email = email;
        settings.DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? "VRINFO" : DisplayName.Trim();
        settings.AlwaysCc = Split(AlwaysCcText);
        settings.ImapHost = ImapHost.Trim();
        settings.ImapPort = imapPort;
        settings.SmtpHost = SmtpHost.Trim();
        settings.SmtpPort = smtpPort;
        settings.StartWithWindows = StartWithWindows;
        settings.EnableTray = EnableTray;
        settings.MinimizeToTray = MinimizeToTray;
        settings.AlwaysConfirmSend = AlwaysConfirmSend;
        settings.AlwaysSendHighPriority = AlwaysSendHighPriority;
        settings.ApplyRulesOnSync = ApplyRulesOnSync;
        settings.FolderInovafarmaEnabled = FolderInovafarmaEnabled;
        settings.FolderHiperEnabled = FolderHiperEnabled;
        settings.FolderContasEnabled = FolderContasEnabled;
        settings.FolderContabilidadeEnabled = FolderContabilidadeEnabled;
        settings.FolderDiscordEnabled = FolderDiscordEnabled;
        settings.InovafarmaTokens = Split(InovafarmaTokens);
        settings.HiperTokens = Split(HiperTokens);
        settings.ContasTokens = Split(ContasTokens);
        settings.ContabilidadeTokens = Split(ContabilidadeTokens);
        settings.DiscordTokens = Split(DiscordTokens);
        settings.ContabilidadeSenders = Split(ContabilidadeSenders);
        settings.UseDarkTheme = _original.UseDarkTheme;
        _store.Save(settings);

        if (!string.IsNullOrWhiteSpace(Password))
            WindowsCredentialStore.Save(email, Password);

        Status = "Salvo. Aplicando…";
        if (window.Owner?.DataContext is ShellViewModel shell)
            await shell.ApplySettingsAsync(settings, Password);
        window.DialogResult = true;
        window.Close();
    }

    private static string Join(IEnumerable<string>? values) =>
        string.Join(Environment.NewLine, values ?? []);

    private static List<string> Split(string? text) =>
        (text ?? "")
            .Split(['\r', '\n', ';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
