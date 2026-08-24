namespace Vrinfo.Mail.Core;

public static class MailConstants
{
    public const string ProductName = "VRINFO Mail";
    public const string DefaultDomain = "vrinfo.com.br";
    public const string DefaultEmail = "thiago@vrinfo.com.br";
    public const string AlwaysCc = "thiago@vrinfo.com.br";

    public const string ImapHost = "imap.uhserver.com";
    public const int ImapPort = 993;
    public const string SmtpHost = "smtps.uhserver.com";
    public const int SmtpPort = 465;

    public const string FolderInovafarma = "Inovafarma";
    public const string FolderHiper = "Hiper";
    public const string FolderAls = "ALS";
    public const string FolderContas = "Contas";
    public const string FolderContabilidade = "Contabilidade";
    public const string FolderDiscord = "Discord";
    public const string FolderDrafts = "Rascunhos";
    public const string KeywordContabilidade = "$VRINFO.CONTABILIDADE";
    public const string CredentialPrefix = "VRINFO.Mail/";
    public const string AutostartTaskName = "VRINFO Mail";

    public const string SignatureUncPath =
        @"\\servidor\SUPORTE\IMG & LOGOS - CLIENTES\IMAGENS & LOGOS\LOGOS-Assinaturas\AssinaturaThiago.png";
    public const string SignatureContentId = "assinatura-thiago";
}
