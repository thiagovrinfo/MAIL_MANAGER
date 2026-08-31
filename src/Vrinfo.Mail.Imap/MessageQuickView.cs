using MimeKit;

namespace Vrinfo.Mail.Imap;

public sealed class RemotePart
{
    public required string Specifier { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public string ContentId { get; init; } = "";
    public long Octets { get; init; }
    public bool IsImage { get; init; }
    public bool IsInline { get; init; }
}

public sealed class MessageQuickView
{
    public required string Folder { get; init; }
    public required uint Uid { get; init; }
    public required string Subject { get; init; }
    public required string From { get; init; }
    public required string Html { get; set; }
    public required string Text { get; init; }
    public required MimeMessage Mime { get; init; }
    public List<RemotePart> Parts { get; } = [];
    public int ByteSize => EncodingSize(Html) + EncodingSize(Text);

    private static int EncodingSize(string value) =>
        string.IsNullOrEmpty(value) ? 0 : value.Length * 2;
}
