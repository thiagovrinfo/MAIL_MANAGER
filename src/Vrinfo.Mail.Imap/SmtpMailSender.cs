using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Vrinfo.Mail.Core;

namespace Vrinfo.Mail.Imap;

public sealed record InlineImagePart(string FilePath, string ContentId);

public static class SmtpMailSender
{
    public static async Task SendAsync(
        MailSettings settings,
        string password,
        IEnumerable<string> to,
        IEnumerable<string>? cc,
        IEnumerable<string>? bcc,
        string subject,
        string textBody,
        string? htmlBody,
        IEnumerable<string>? attachmentPaths,
        IEnumerable<InlineImagePart>? inlineImages,
        MimeMessage? inReplyTo,
        bool highPriority,
        bool requestReadReceipt,
        IProgress<(int Percent, string Status)>? progress,
        CancellationToken cancellationToken)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(
            string.IsNullOrWhiteSpace(settings.DisplayName) ? settings.Email : settings.DisplayName,
            settings.Email));

        foreach (var address in to.Where(EmailAddressHelper.IsValid))
            message.To.Add(MailboxAddress.Parse(address));

        var ccSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var address in cc ?? [])
        {
            if (EmailAddressHelper.IsValid(address))
                ccSet.Add(address.Trim());
        }

        foreach (var address in settings.AlwaysCc ?? [])
        {
            if (EmailAddressHelper.IsValid(address))
                ccSet.Add(address.Trim());
        }

        ccSet.Remove(settings.Email);
        foreach (var address in ccSet)
            message.Cc.Add(MailboxAddress.Parse(address));

        foreach (var address in bcc ?? [])
        {
            if (EmailAddressHelper.IsValid(address))
                message.Bcc.Add(MailboxAddress.Parse(address.Trim()));
        }

        progress?.Report((8, "Preparando destinatários…"));
        message.Subject = subject ?? string.Empty;
        if (highPriority)
        {
            message.Priority = MessagePriority.Urgent;
            message.XPriority = XMessagePriority.High;
            message.Importance = MessageImportance.High;
        }

        if (requestReadReceipt && EmailAddressHelper.IsValid(settings.Email))
            message.Headers[HeaderId.DispositionNotificationTo] = settings.Email;

        if (inReplyTo is not null)
        {
            if (!string.IsNullOrWhiteSpace(inReplyTo.MessageId))
                message.InReplyTo = inReplyTo.MessageId;
            foreach (var id in inReplyTo.References)
                message.References.Add(id);
            if (!string.IsNullOrWhiteSpace(inReplyTo.MessageId))
                message.References.Add(inReplyTo.MessageId);
        }

        progress?.Report((18, "Montando o corpo da mensagem…"));
        var builder = new BodyBuilder
        {
            TextBody = textBody ?? string.Empty
        };
        if (!string.IsNullOrWhiteSpace(htmlBody))
            builder.HtmlBody = htmlBody;

        progress?.Report((28, "Incluindo imagens e assinatura…"));
        foreach (var inline in inlineImages ?? [])
        {
            if (!File.Exists(inline.FilePath))
                continue;
            var resource = builder.LinkedResources.Add(Path.GetFullPath(inline.FilePath));
            resource.ContentId = inline.ContentId;
            resource.ContentDisposition = new ContentDisposition(ContentDisposition.Inline);
        }

        var files = (attachmentPaths ?? []).Where(File.Exists).ToList();
        if (files.Count > 0)
            progress?.Report((38, files.Count == 1 ? "Anexando 1 arquivo…" : $"Anexando {files.Count} arquivos…"));
        foreach (var path in files)
            builder.Attachments.Add(path);

        message.Body = builder.ToMessageBody();

        using var client = new SmtpClient { Timeout = 180_000 };
        progress?.Report((52, "Conectando ao servidor SMTP…"));
        await client.ConnectAsync(settings.SmtpHost, settings.SmtpPort, SecureSocketOptions.SslOnConnect, cancellationToken);
        progress?.Report((68, "Autenticando conta…"));
        await client.AuthenticateAsync(settings.Email, password, cancellationToken);
        progress?.Report((82, "Transmitindo e-mail…"));
        await client.SendAsync(message, cancellationToken);
        progress?.Report((94, "Encerrando conexão…"));
        await client.DisconnectAsync(true, cancellationToken);
        progress?.Report((100, "Enviado com sucesso"));
    }
}
