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
    public bool EnableTray { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool AlwaysConfirmSend { get; set; }
    public bool AlwaysSendHighPriority { get; set; }
    public bool ApplyRulesOnSync { get; set; } = true;
    public bool UseDarkTheme { get; set; }
    public bool FoldersExpanded { get; set; } = true;
    public bool FolderInovafarmaEnabled { get; set; } = true;
    public bool FolderHiperEnabled { get; set; } = true;
    public bool FolderContasEnabled { get; set; } = true;
    public bool FolderContabilidadeEnabled { get; set; } = true;
    public bool FolderDiscordEnabled { get; set; } = true;
    public List<string> AlwaysCc { get; set; } = [MailConstants.AlwaysCc];
    public List<string> ContabilidadeSenders { get; set; } = [];
    public Dictionary<string, string> SenderFolderRules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> InovafarmaTokens { get; set; } = [];
    public List<string> HiperTokens { get; set; } = [];
    public List<string> ContasTokens { get; set; } = [];
    public List<string> ContabilidadeTokens { get; set; } = [];
    public List<string> DiscordTokens { get; set; } = [];
}
