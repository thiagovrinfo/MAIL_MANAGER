using System.Text.RegularExpressions;

namespace Vrinfo.Mail.App;

internal static class MailHtml
{
    public static string Wrap(string? html, bool dark)
    {
        html ??= "<p></p>";
        var bg = dark ? "#121820" : "#FFFFFF";
        var fg = dark ? "#F1F5F9" : "#15202B";
        var link = dark ? "#8EC5FF" : "#0D47A1";
        var scheme = dark ? "dark" : "light";
        var css =
            $"html{{color-scheme:{scheme};background:{bg}!important}}" +
            $"body{{background:{bg}!important;color:{fg}!important;" +
            "font-family:'Segoe UI',Calibri,Arial,sans-serif;font-size:16px;line-height:1.55}}" +
            $"a{{color:{link}!important}}" +
            "img{max-width:100%;height:auto}" +
            "pre,code{color:inherit;white-space:pre-wrap}" +
            "blockquote{color:inherit}";

        var tag = $"<style id=\"vrinfo-theme\">{css}</style>";
        if (Regex.IsMatch(html, "<html", RegexOptions.IgnoreCase))
        {
            html = Regex.Replace(html, "<style id=\"vrinfo-theme\">[\\s\\S]*?</style>", "", RegexOptions.IgnoreCase);
            if (Regex.IsMatch(html, "</head>", RegexOptions.IgnoreCase))
                return Regex.Replace(html, "</head>", tag + "</head>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            return Regex.Replace(html, "<html[^>]*>", m => m.Value + "<head>" + tag + "</head>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        }

        return "<!DOCTYPE html><html><head><meta charset=\"utf-8\">" + tag + "</head><body>" + html + "</body></html>";
    }
}
