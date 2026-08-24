namespace Vrinfo.Mail.Core;

public sealed class RuleMatchInput
{
    public string From { get; init; } = string.Empty;
    public string To { get; init; } = string.Empty;
    public string Cc { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public IReadOnlyList<string> ContabilidadeSenders { get; init; } = [];
    public bool HasContabilidadeKeyword { get; init; }
}
