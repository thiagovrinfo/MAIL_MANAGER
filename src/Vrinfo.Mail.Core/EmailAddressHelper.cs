namespace Vrinfo.Mail.Core;

public static class EmailAddressHelper
{
    public static string CompleteVrinfoAddress(string? input)
    {
        var value = (input ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        if (!value.Contains('@', StringComparison.Ordinal))
            return value + "@" + MailConstants.DefaultDomain;

        return value;
    }

    public static bool IsValid(string? email)
    {
        var value = (email ?? string.Empty).Trim();
        var at = value.IndexOf('@');
        return at > 0 && at < value.Length - 1 && value.IndexOf('@', at + 1) < 0;
    }
}
