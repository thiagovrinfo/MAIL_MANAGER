namespace Vrinfo.Mail.Core;

public sealed class RuleMatchInput
{
    public string From { get; init; } = string.Empty;
    public string To { get; init; } = string.Empty;
    public string Cc { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public IReadOnlyList<string> ContabilidadeSenders { get; init; } = [];
    public IReadOnlyDictionary<string, string> SenderFolderRules { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<string> InovafarmaTokens { get; init; } = [];
    public IReadOnlyList<string> HiperTokens { get; init; } = [];
    public IReadOnlyList<string> ContasTokens { get; init; } = [];
    public IReadOnlyList<string> ContabilidadeTokens { get; init; } = [];
    public IReadOnlyList<string> DiscordTokens { get; init; } = [];
    public bool FolderInovafarmaEnabled { get; init; } = true;
    public bool FolderHiperEnabled { get; init; } = true;
    public bool FolderContasEnabled { get; init; } = true;
    public bool FolderContabilidadeEnabled { get; init; } = true;
    public bool FolderDiscordEnabled { get; init; } = true;
    public bool HasContabilidadeKeyword { get; init; }
}
