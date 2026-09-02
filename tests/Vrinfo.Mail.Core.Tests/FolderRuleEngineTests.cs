using Vrinfo.Mail.Core;
using Xunit;

namespace Vrinfo.Mail.Core.Tests;

public sealed class FolderRuleEngineTests
{
    [Fact]
    public void Discord_goes_to_discord_folder()
    {
        var kind = FolderRuleEngine.ResolveFolder(new RuleMatchInput
        {
            From = "noreply@discord.com",
            Subject = "Você perdeu uma mensagem"
        });
        Assert.Equal(SmartFolderKind.Discord, kind);
    }

    [Fact]
    public void Inovafarma_goes_to_inovafarma_and_high_priority()
    {
        var input = new RuleMatchInput
        {
            From = "alertas@inovafarma.com.br",
            Subject = "Atualização do sistema"
        };
        var kind = FolderRuleEngine.ResolveFolder(input);
        Assert.Equal(SmartFolderKind.Inovafarma, kind);
        Assert.Equal(MessagePriorityLevel.High, FolderRuleEngine.ResolvePriority(input, kind));
    }

    [Fact]
    public void Zendesk_representante_goes_to_inovafarma()
    {
        var kind = FolderRuleEngine.ResolveFolder(new RuleMatchInput
        {
            From = "support@atendimentoaorepresentante.zendesk.com",
            Subject = "Ticket #123"
        });
        Assert.Equal(SmartFolderKind.Inovafarma, kind);
    }

    [Fact]
    public void Als_and_contas_and_baccarin()
    {
        Assert.Equal(SmartFolderKind.Inovafarma, FolderRuleEngine.ResolveFolder(new RuleMatchInput
        {
            From = "Marilice.Santos@ALSGlobal.com",
            Subject = "Laudo"
        }));
        Assert.Equal(SmartFolderKind.Contas, FolderRuleEngine.ResolveFolder(new RuleMatchInput
        {
            From = "no-reply@accounts.google.com",
            Subject = "Alerta de segurança"
        }));
        Assert.Equal(SmartFolderKind.Contabilidade, FolderRuleEngine.ResolveFolder(new RuleMatchInput
        {
            From = "silvia@escritoriobaccarin.com.br",
            Subject = "Honorários"
        }));
    }

    [Fact]
    public void Hiper_goes_to_hiper_folder()
    {
        var kind = FolderRuleEngine.ResolveFolder(new RuleMatchInput
        {
            From = "noreply@hiper.com.br",
            Subject = "Atualização do sistema Hiper"
        });
        Assert.Equal(SmartFolderKind.Hiper, kind);
    }

    [Fact]
    public void Contabilidade_tag_is_retroactive_and_future()
    {
        var input = new RuleMatchInput
        {
            From = "contato@escritorioalfa.com.br",
            Subject = "Honorários",
            ContabilidadeSenders = ["escritorioalfa.com.br"],
            HasContabilidadeKeyword = false
        };
        Assert.Equal(SmartFolderKind.Contabilidade, FolderRuleEngine.ResolveFolder(input));

        var tagged = new RuleMatchInput
        {
            From = "qualquer@empresa.com",
            Subject = "Balancete",
            HasContabilidadeKeyword = true
        };
        Assert.Equal(SmartFolderKind.Contabilidade, FolderRuleEngine.ResolveFolder(tagged));
    }

    [Fact]
    public void Extra_rule_token_moves_to_folder()
    {
        var kind = FolderRuleEngine.ResolveFolder(new RuleMatchInput
        {
            From = "avisos@parceiro.com",
            Subject = "Pedido especial XPTO",
            HiperTokens = ["xpto"],
            FolderHiperEnabled = true
        });
        Assert.Equal(SmartFolderKind.Hiper, kind);
    }

    [Fact]
    public void Sender_rule_uses_exact_address_and_overrides_smart_rules()
    {
        var destination = FolderRuleEngine.ResolveDestination(new RuleMatchInput
        {
            From = "ABC@adm.com.br",
            Subject = "Discord notification",
            SenderFolderRules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["abc@adm.com.br"] = MailConstants.FolderContabilidade
            }
        });
        Assert.Equal(MailConstants.FolderContabilidade, destination);
    }

    [Fact]
    public void Disabled_folder_skips_rule()
    {
        var kind = FolderRuleEngine.ResolveFolder(new RuleMatchInput
        {
            From = "noreply@discord.com",
            Subject = "Você perdeu uma mensagem",
            FolderDiscordEnabled = false
        });
        Assert.Equal(SmartFolderKind.None, kind);
    }

    [Fact]
    public void Completes_vrinfo_domain()
    {
        Assert.Equal("thiago@vrinfo.com.br", EmailAddressHelper.CompleteVrinfoAddress("thiago"));
        Assert.True(EmailAddressHelper.IsValid("thiago@vrinfo.com.br"));
    }

    [Fact]
    public void Fiscal_zip_is_detected()
    {
        Assert.True(FolderRuleEngine.LooksFiscal("Arquivos fiscais março", ["nfe-2026.zip"]));
    }
}
