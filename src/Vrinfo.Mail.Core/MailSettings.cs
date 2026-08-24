namespace Vrinfo.Mail.Core;

public sealed class MailSettings
{
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = "VRINFO";
    public string ImapHost { get; set; } = MailConstants.ImapHost;
    public int ImapPort { get; set; } = MailConstants.ImapPort;
    public string SmtpHost { get; set; } = MailConstants.SmtpHost;
    public int SmtpPort { get; set; } = MailConstants.SmtpPort;
    public bool StartWithWindows { get; set; } = true;
    public List<string> ContabilidadeSenders { get; set; } = [];
    public bool ApplyRulesOnSync { get; set; } = true;
    public bool UseDarkTheme { get; set; }
}
