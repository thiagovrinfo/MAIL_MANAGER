using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Vrinfo.Mail.App;

internal static class FlowDocumentHtml
{
    public readonly record struct Result(string Html, string Text, List<(string Path, string Cid)> Images);

    public static Result Convert(FlowDocument document)
    {
        var html = new StringBuilder();
        var text = new StringBuilder();
        var images = new List<(string Path, string Cid)>();
        html.Append("""<div style="font-family:Calibri,Arial,sans-serif;font-size:14px;color:#222;">""");
        foreach (var block in document.Blocks)
            WriteBlock(block, html, text, images);
        html.Append("</div>");
        return new Result(html.ToString(), text.ToString().Trim(), images);
    }

    private static void WriteBlock(
        Block block,
        StringBuilder html,
        StringBuilder text,
        List<(string Path, string Cid)> images)
    {
        switch (block)
        {
            case List list:
                html.Append(list.MarkerStyle == TextMarkerStyle.Decimal ? "<ol>" : "<ul>");
                foreach (var item in list.ListItems)
                {
                    html.Append("<li>");
                    foreach (var child in item.Blocks)
                        WriteBlock(child, html, text, images);
                    html.Append("</li>");
                    text.AppendLine();
                }
                html.Append(list.MarkerStyle == TextMarkerStyle.Decimal ? "</ol>" : "</ul>");
                break;
            case Paragraph paragraph:
                html.Append("<p style=\"").Append(ParagraphStyle(paragraph)).Append("\">");
                foreach (var inline in paragraph.Inlines)
                    WriteInline(inline, html, text, images);
                html.Append("</p>");
                text.AppendLine();
                break;
            case Section section:
                foreach (var child in section.Blocks)
                    WriteBlock(child, html, text, images);
                break;
        }
    }

    private static void WriteInline(
        Inline inline,
        StringBuilder html,
        StringBuilder text,
        List<(string Path, string Cid)> images)
    {
        switch (inline)
        {
            case LineBreak:
                html.Append("<br/>");
                text.AppendLine();
                break;
            case Run run:
                html.Append(Span(run.Text, run));
                text.Append(run.Text);
                break;
            case Bold bold:
                html.Append("<strong>");
                foreach (var child in bold.Inlines)
                    WriteInline(child, html, text, images);
                html.Append("</strong>");
                break;
            case Italic italic:
                html.Append("<em>");
                foreach (var child in italic.Inlines)
                    WriteInline(child, html, text, images);
                html.Append("</em>");
                break;
            case Underline underline:
                html.Append("<u>");
                foreach (var child in underline.Inlines)
                    WriteInline(child, html, text, images);
                html.Append("</u>");
                break;
            case Hyperlink link:
                var href = link.NavigateUri?.ToString() ?? "#";
                html.Append("<a href=\"").Append(Enc(href)).Append("\">");
                foreach (var child in link.Inlines)
                    WriteInline(child, html, text, images);
                html.Append("</a>");
                break;
            case Span span:
                html.Append("<span style=\"").Append(InlineStyle(span)).Append("\">");
                foreach (var child in span.Inlines)
                    WriteInline(child, html, text, images);
                html.Append("</span>");
                break;
            case InlineUIContainer container when container.Child is System.Windows.Controls.Image image:
                var path = ResolveImagePath(image);
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    image.Tag = path;
                    var cid = "img-" + Guid.NewGuid().ToString("N");
                    images.Add((path, cid));
                    var width = image.MaxWidth is double.NaN or 0 ? 560 : image.MaxWidth;
                    html.Append("<img src=\"cid:")
                        .Append(cid)
                        .Append("\" alt=\"imagem\" style=\"max-width:")
                        .Append(((int)width).ToString(CultureInfo.InvariantCulture))
                        .Append("px;height:auto;border:0\"/>");
                    text.Append("[imagem]");
                }
                break;
        }
    }

    private static string? ResolveImagePath(System.Windows.Controls.Image image)
    {
        if (image.Tag is string tagged && !string.IsNullOrWhiteSpace(tagged))
        {
            try
            {
                var full = Path.GetFullPath(tagged);
                if (File.Exists(full))
                    return full;
            }
            catch
            {
                // tag não é caminho
            }
        }

        if (image.Source is BitmapImage bitmap && bitmap.UriSource is Uri uri)
        {
            try
            {
                string? file = null;
                if (uri.IsAbsoluteUri && uri.IsFile)
                    file = uri.LocalPath;
                else if (!uri.IsAbsoluteUri)
                    file = Path.GetFullPath(uri.OriginalString.Replace('/', Path.DirectorySeparatorChar));

                if (file is not null && File.Exists(file))
                    return file;
            }
            catch (InvalidOperationException)
            {
                // URI relativa sem LocalPath
            }
            catch
            {
                // ignora e tenta persistir o bitmap
            }
        }

        return image.Source is BitmapSource source ? PersistBitmap(source) : null;
    }

    private static string PersistBitmap(BitmapSource source)
    {
        var dest = Path.Combine(Path.GetTempPath(), "vrinfo-mail-inline-" + Guid.NewGuid().ToString("N") + ".jpg");
        var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = File.Create(dest);
        encoder.Save(stream);
        return dest;
    }

    private static string ParagraphStyle(Paragraph paragraph)
    {
        var align = paragraph.TextAlignment switch
        {
            TextAlignment.Center => "center",
            TextAlignment.Right => "right",
            TextAlignment.Justify => "justify",
            _ => "left"
        };
        return "margin:0 0 10px 0;text-align:" + align + ";";
    }

    private static string Span(string? content, Inline inline)
        => "<span style=\"" + InlineStyle(inline) + "\">" + Enc(content) + "</span>";

    private static string InlineStyle(Inline inline)
    {
        var css = new StringBuilder();
        if (inline.FontFamily is not null)
            css.Append("font-family:'").Append(Enc(inline.FontFamily.Source)).Append("',Arial,sans-serif;");
        if (inline.FontSize > 0)
            css.Append("font-size:").Append(Math.Round(inline.FontSize * 0.75, 1).ToString(CultureInfo.InvariantCulture)).Append("pt;");
        if (inline.FontWeight.ToOpenTypeWeight() >= FontWeights.Bold.ToOpenTypeWeight())
            css.Append("font-weight:700;");
        if (inline.FontStyle == FontStyles.Italic)
            css.Append("font-style:italic;");
        if (inline.TextDecorations?.Any(d => d.Location == TextDecorationLocation.Underline) == true)
            css.Append("text-decoration:underline;");
        if (inline.Foreground is SolidColorBrush brush)
            css.Append("color:").Append(ToHex(brush.Color)).Append(';');
        if (inline.Background is SolidColorBrush back)
            css.Append("background:").Append(ToHex(back.Color)).Append(';');
        return css.ToString();
    }

    private static string ToHex(System.Windows.Media.Color color)
        => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static string Enc(string? value)
        => System.Net.WebUtility.HtmlEncode(value ?? "");
}
