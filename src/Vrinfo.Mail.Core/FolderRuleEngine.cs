namespace Vrinfo.Mail.Core;

public static class FolderRuleEngine
{
    public static SmartFolderKind ResolveFolder(RuleMatchInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.FolderDiscordEnabled && IsDiscord(input))
            return SmartFolderKind.Discord;

        if (input.FolderInovafarmaEnabled && IsInovafarma(input))
            return SmartFolderKind.Inovafarma;

        if (input.FolderHiperEnabled && IsHiper(input))
            return SmartFolderKind.Hiper;

        if (input.FolderContasEnabled && IsContas(input))
            return SmartFolderKind.Contas;

        if (input.FolderContabilidadeEnabled && IsContabilidade(input))
            return SmartFolderKind.Contabilidade;

        return SmartFolderKind.None;
    }

    public static string? FolderName(SmartFolderKind kind) => kind switch
    {
        SmartFolderKind.Inovafarma => MailConstants.FolderInovafarma,
        SmartFolderKind.Hiper => MailConstants.FolderHiper,
        SmartFolderKind.Contas => MailConstants.FolderContas,
        SmartFolderKind.Contabilidade => MailConstants.FolderContabilidade,
        SmartFolderKind.Discord => MailConstants.FolderDiscord,
        _ => null
    };

    public static MessagePriorityLevel ResolvePriority(RuleMatchInput input, SmartFolderKind folder)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (folder == SmartFolderKind.Inovafarma || IsInovafarma(input))
            return MessagePriorityLevel.High;

        var haystack = Haystack(input);
        if (haystack.Contains("urgente", StringComparison.Ordinal) ||
            haystack.Contains("urgent", StringComparison.Ordinal) ||
            haystack.Contains("vencid", StringComparison.Ordinal))
            return MessagePriorityLevel.High;

        return MessagePriorityLevel.Normal;
    }

    public static bool IsDiscord(RuleMatchInput input)
    {
        var haystack = Haystack(input);
        return haystack.Contains("discord.com", StringComparison.Ordinal) ||
               haystack.Contains("discordapp.com", StringComparison.Ordinal) ||
               haystack.Contains("@discord.", StringComparison.Ordinal) ||
               ContainsAny(haystack, input.DiscordTokens);
    }

    public static bool IsInovafarma(RuleMatchInput input)
    {
        var haystack = Haystack(input);
        return haystack.Contains("inovafarma", StringComparison.Ordinal) ||
               (haystack.Contains("zendesk.com", StringComparison.Ordinal) &&
                haystack.Contains("representante", StringComparison.Ordinal)) ||
               haystack.Contains("atendimento ao representante", StringComparison.Ordinal) ||
               IsAls(input) ||
               ContainsAny(haystack, input.InovafarmaTokens);
    }

    public static bool IsHiper(RuleMatchInput input)
    {
        var haystack = Haystack(input);
        return haystack.Contains("hiper.com", StringComparison.Ordinal) ||
               haystack.Contains("@hiper.", StringComparison.Ordinal) ||
               haystack.Contains(" hiper ", StringComparison.Ordinal) ||
               haystack.Contains("sistema hiper", StringComparison.Ordinal) ||
               haystack.Contains("sistemas hiper", StringComparison.Ordinal) ||
               haystack.Contains("hiper pdv", StringComparison.Ordinal) ||
               haystack.Contains("linx hiper", StringComparison.Ordinal) ||
               ContainsAny(haystack, input.HiperTokens);
    }

    public static bool IsAls(RuleMatchInput input)
    {
        var haystack = Haystack(input);
        return haystack.Contains("alsglobal.com", StringComparison.Ordinal) ||
               haystack.Contains("@alsglobal.", StringComparison.Ordinal);
    }

    public static bool IsContas(RuleMatchInput input)
    {
        var haystack = Haystack(input);
        return haystack.Contains("accounts.google.com", StringComparison.Ordinal) ||
               haystack.Contains("no-reply@accounts.google.com", StringComparison.Ordinal) ||
               haystack.Contains("accountprotection.microsoft.com", StringComparison.Ordinal) ||
               haystack.Contains("account-security-noreply@accountprotection.microsoft.com", StringComparison.Ordinal) ||
               ContainsAny(haystack, input.ContasTokens);
    }

    public static bool IsContabilidade(RuleMatchInput input)
    {
        if (input.HasContabilidadeKeyword)
            return true;

        var haystack = Haystack(input);
        if (haystack.Contains("contabil", StringComparison.Ordinal) ||
            haystack.Contains("escritorio contabil", StringComparison.Ordinal) ||
            haystack.Contains("escritório contábil", StringComparison.Ordinal) ||
            haystack.Contains("escritoriobaccarin.com.br", StringComparison.Ordinal) ||
            haystack.Contains("rr.contabil@", StringComparison.Ordinal) ||
            haystack.Contains("ribeiroorganizacaocontabil.com.br", StringComparison.Ordinal) ||
            haystack.Contains("fiscal.io", StringComparison.Ordinal) ||
            haystack.Contains("veritascontabilidade.com", StringComparison.Ordinal))
            return true;

        var from = (input.From ?? "").ToLowerInvariant();
        if (from.StartsWith("fiscal@", StringComparison.Ordinal) && from.EndsWith(".com.br", StringComparison.Ordinal))
            return true;

        foreach (var sender in input.ContabilidadeSenders)
        {
            var token = sender.Trim().ToLowerInvariant();
            if (token.Length == 0)
                continue;
            if (haystack.Contains(token, StringComparison.Ordinal))
                return true;
        }

        return ContainsAny(haystack, input.ContabilidadeTokens);
    }

    private static bool ContainsAny(string haystack, IEnumerable<string>? tokens)
    {
        foreach (var raw in tokens ?? [])
        {
            var token = raw.Trim().ToLowerInvariant();
            if (token.Length < 3)
                continue;
            if (haystack.Contains(token, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    public static bool LooksFiscal(string? subject, IEnumerable<string>? attachmentNames)
    {
        var haystack = (subject ?? string.Empty).ToLowerInvariant();
        if (haystack.Contains("nf-e", StringComparison.Ordinal) ||
            haystack.Contains("nfe", StringComparison.Ordinal) ||
            haystack.Contains("arquivo fiscal", StringComparison.Ordinal) ||
            haystack.Contains("arquivos fiscais", StringComparison.Ordinal) ||
            (haystack.Contains("xml", StringComparison.Ordinal) && haystack.Contains("fiscal", StringComparison.Ordinal)))
            return true;

        foreach (var name in attachmentNames ?? [])
        {
            var n = name.ToLowerInvariant();
            if (n.EndsWith(".zip", StringComparison.Ordinal) && (n.Contains("nfe") || n.Contains("fiscal") || n.Contains("xml")))
                return true;
            if (n.EndsWith(".xml", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    public static string Haystack(RuleMatchInput input)
        => $"{input.From} {input.To} {input.Cc} {input.Subject}".ToLowerInvariant();
}
