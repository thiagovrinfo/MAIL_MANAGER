using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using Vrinfo.Mail.Core;
using FolderRuleEngine = Vrinfo.Mail.Core.FolderRuleEngine;
using RuleMatchInput = Vrinfo.Mail.Core.RuleMatchInput;

namespace Vrinfo.Mail.Imap;

public sealed class ImapMailbox : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ImapClient? _client;
    private MailSettings _settings = new();
    private string _password = string.Empty;

    public async Task ConnectAsync(MailSettings settings, string password, CancellationToken cancellationToken)
    {
        _settings = settings;
        _password = password;
        await EnsureConnectedAsync(cancellationToken);
        await EnsureSmartFoldersAsync(cancellationToken);
    }

    public async Task EnsureSmartFoldersAsync(CancellationToken cancellationToken)
    {
        var client = await EnsureConnectedAsync(cancellationToken);
        var personal = client.GetFolder(client.PersonalNamespaces[0]);
        foreach (var name in new[]
                 {
                     MailConstants.FolderInovafarma,
                     MailConstants.FolderHiper,
                     MailConstants.FolderContas,
                     MailConstants.FolderContabilidade,
                     MailConstants.FolderDiscord,
                     MailConstants.FolderDrafts
                 })
        {
            await GetOrCreateFolderAsync(personal, name, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<IndexedMessage>> SyncFolderAsync(
        string folderName,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var client = await EnsureConnectedAsync(cancellationToken);
            var folder = await OpenAsync(client, folderName, FolderAccess.ReadOnly, cancellationToken);
            var uids = await folder.SearchAsync(SearchQuery.All, cancellationToken);
            var list = new List<IndexedMessage>();
            foreach (var batch in uids.Chunk(200))
            {
                var summaries = await folder.FetchAsync(
                    batch.ToList(),
                    MessageSummaryItems.Envelope | MessageSummaryItems.Flags | MessageSummaryItems.UniqueId,
                    cancellationToken);
                foreach (var summary in summaries)
                    list.Add(ToIndexed(folder.FullName, summary, _settings));
            }

            return list;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ApplyInboxRulesAsync(Action<IndexedMessage>? onMoved, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var client = await EnsureConnectedAsync(cancellationToken);
            await ApplyRulesUntilEmptyAsync(client, client.Inbox, onMoved, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ApplyMailboxSmartRulesAsync(Action<IndexedMessage>? onMoved, CancellationToken cancellationToken)
    {
        await ApplyInboxRulesAsync(onMoved, cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var client = await EnsureConnectedAsync(cancellationToken);
            var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                MailConstants.FolderInovafarma,
                MailConstants.FolderHiper,
                MailConstants.FolderContas,
                MailConstants.FolderContabilidade,
                MailConstants.FolderDiscord
            };

            var personal = client.GetFolder(client.PersonalNamespaces[0]);
            var folders = (await personal.GetSubfoldersAsync(false, cancellationToken)).ToList();
            foreach (var folder in folders)
            {
                if (destinations.Contains(folder.Name) || destinations.Contains(folder.FullName))
                    continue;
                if (folder.Attributes.HasFlag(FolderAttributes.Sent) ||
                    folder.Attributes.HasFlag(FolderAttributes.Trash) ||
                    folder.Attributes.HasFlag(FolderAttributes.Drafts) ||
                    folder.Attributes.HasFlag(FolderAttributes.Junk) ||
                    folder.Name.Equals(MailConstants.FolderDrafts, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken);
                }
                catch
                {
                    continue;
                }

                await ApplyRulesUntilEmptyAsync(client, folder, onMoved, cancellationToken);
            }

            try
            {
                var als = folders.FirstOrDefault(f =>
                    f.Name.Equals(MailConstants.FolderAls, StringComparison.OrdinalIgnoreCase));
                if (als is not null)
                {
                    try
                    {
                        await als.CloseAsync(false, cancellationToken);
                    }
                    catch
                    {
                    }

                    await als.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
                    var leftover = await als.SearchAsync(SearchQuery.All, cancellationToken);
                    await als.CloseAsync(false, cancellationToken);
                    if (leftover.Count == 0)
                        await als.DeleteAsync(cancellationToken);
                }
            }
            catch
            {
                // pasta ALS antiga pode permanecer vazia no servidor
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<SenderCount>> CountInboxSendersAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var client = await EnsureConnectedAsync(cancellationToken);
            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
            var uids = await inbox.SearchAsync(SearchQuery.All, cancellationToken);
            var counts = new Dictionary<string, (string Name, int Count)>(StringComparer.OrdinalIgnoreCase);
            foreach (var batch in uids.Chunk(200))
            {
                var summaries = await inbox.FetchAsync(
                    batch.ToList(),
                    MessageSummaryItems.Envelope,
                    cancellationToken);
                foreach (var summary in summaries)
                {
                    var box = summary.Envelope?.From?.Mailboxes.FirstOrDefault();
                    var address = (box?.Address ?? "(sem remetente)").Trim();
                    if (!counts.TryGetValue(address, out var current))
                        current = (box?.Name ?? address, 0);
                    counts[address] = (string.IsNullOrWhiteSpace(current.Name) ? address : current.Name, current.Count + 1);
                }
            }

            return counts
                .Select(p => new SenderCount(p.Key, p.Value.Name, p.Value.Count))
                .OrderByDescending(s => s.Count)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MoveAsync(string folderName, uint uid, string destination, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var client = await EnsureConnectedAsync(cancellationToken);
            var source = await OpenAsync(client, folderName, FolderAccess.ReadWrite, cancellationToken);
            var dest = await FindFolderAsync(client, destination, cancellationToken)
                       ?? throw new InvalidOperationException("Pasta de destino não encontrada.");
            await source.MoveToAsync(new UniqueId(uid), dest, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetSeenAsync(string folderName, uint uid, bool seen, CancellationToken cancellationToken)
    {
        await MutateFlagsAsync(folderName, uid, MessageFlags.Seen, seen, cancellationToken);
    }

    public async Task SetFlaggedAsync(string folderName, uint uid, bool flagged, CancellationToken cancellationToken)
    {
        await MutateFlagsAsync(folderName, uid, MessageFlags.Flagged, flagged, cancellationToken);
    }

    public async Task<uint> SaveDraftAsync(MimeMessage message, uint? replaceUid, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var client = await EnsureConnectedAsync(cancellationToken);
            var personal = client.GetFolder(client.PersonalNamespaces[0]);
            var folder = await GetOrCreateFolderAsync(personal, MailConstants.FolderDrafts, cancellationToken);
            await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

            var appended = await folder.AppendAsync(
                message,
                MessageFlags.Draft | MessageFlags.Seen,
                cancellationToken);
            var newUid = appended?.Id ?? 0;

            if (replaceUid is uint oldUid && oldUid != 0 && oldUid != newUid)
            {
                try
                {
                    await folder.AddFlagsAsync(new UniqueId(oldUid), MessageFlags.Deleted, true, cancellationToken);
                    await folder.ExpungeAsync(cancellationToken);
                }
                catch
                {
                    // UID antigo já não existe
                }
            }

            return newUid != 0 ? newUid : replaceUid ?? 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(string folderName, uint uid, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var client = await EnsureConnectedAsync(cancellationToken);
            var folder = await OpenAsync(client, folderName, FolderAccess.ReadWrite, cancellationToken);
            var trash = client.GetFolder(SpecialFolder.Trash);
            if (trash is not null && !string.Equals(folder.FullName, trash.FullName, StringComparison.OrdinalIgnoreCase))
            {
                await folder.MoveToAsync(new UniqueId(uid), trash, cancellationToken);
                return;
            }

            await folder.AddFlagsAsync(new UniqueId(uid), MessageFlags.Deleted, true, cancellationToken);
            await folder.ExpungeAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task EmptyTrashAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var client = await EnsureConnectedAsync(cancellationToken);
            var trash = await OpenAsync(client, "Trash", FolderAccess.ReadWrite, cancellationToken);
            var uids = await trash.SearchAsync(SearchQuery.All, cancellationToken);
            if (uids.Count == 0)
                return;
            await trash.AddFlagsAsync(uids, MessageFlags.Deleted, true, cancellationToken);
            await trash.ExpungeAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkFolderReadAsync(string folderName, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var client = await EnsureConnectedAsync(cancellationToken);
            var folder = await OpenAsync(client, folderName, FolderAccess.ReadWrite, cancellationToken);
            var uids = await folder.SearchAsync(SearchQuery.NotSeen, cancellationToken);
            if (uids.Count == 0)
                return;
            await folder.AddFlagsAsync(uids, MessageFlags.Seen, true, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkMailboxReadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var client = await EnsureConnectedAsync(cancellationToken);
            var folders = new List<IMailFolder> { client.Inbox };
            try
            {
                var personal = client.GetFolder(client.PersonalNamespaces[0]);
                folders.AddRange(await personal.GetSubfoldersAsync(false, cancellationToken));
            }
            catch
            {
                // namespace pessoal indisponível
            }

            foreach (var folder in folders)
            {
                if (folder.Attributes.HasFlag(FolderAttributes.Drafts) ||
                    folder.Name.Equals(MailConstants.FolderDrafts, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken);
                    var uids = await folder.SearchAsync(SearchQuery.NotSeen, cancellationToken);
                    foreach (var batch in uids.Chunk(400))
                        await folder.AddFlagsAsync(batch.ToList(), MessageFlags.Seen, true, cancellationToken);
                }
                catch
                {
                    // pasta sem permissão de escrita
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AddContabilidadeKeywordAsync(string folderName, uint uid, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var client = await EnsureConnectedAsync(cancellationToken);
            var folder = await OpenAsync(client, folderName, FolderAccess.ReadWrite, cancellationToken);
            try
            {
                // servidores UOL podem não persistir keywords IMAP
            }
            catch
            {
                // alguns servidores IMAP ignoram keywords
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MimeMessage> GetMessageAsync(string folderName, uint uid, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var client = await EnsureConnectedAsync(cancellationToken);
            var folder = await OpenAsync(client, folderName, FolderAccess.ReadOnly, cancellationToken);
            return await folder.GetMessageAsync(new UniqueId(uid), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CleanupNewslettersAsync(string folderName, int olderThanDays, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var client = await EnsureConnectedAsync(cancellationToken);
            var folder = await OpenAsync(client, folderName, FolderAccess.ReadWrite, cancellationToken);
            var cutoff = DateTime.Now.AddDays(-olderThanDays);
            var uids = await folder.SearchAsync(SearchQuery.DeliveredBefore(cutoff), cancellationToken);
            if (uids.Count == 0)
                return;

            var summaries = await folder.FetchAsync(
                uids,
                MessageSummaryItems.UniqueId,
                new[] { HeaderId.ListUnsubscribe },
                cancellationToken);

            var move = summaries
                .Where(s => !string.IsNullOrWhiteSpace(s.Headers?[HeaderId.ListUnsubscribe]))
                .Select(s => s.UniqueId)
                .ToList();
            if (move.Count == 0)
                return;

            var trash = client.GetFolder(SpecialFolder.Trash);
            if (trash is null)
            {
                await folder.AddFlagsAsync(move, MessageFlags.Deleted, true, cancellationToken);
                await folder.ExpungeAsync(cancellationToken);
                return;
            }

            await folder.MoveToAsync(move, trash, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ApplyContabilidadeRetroactiveAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var client = await EnsureConnectedAsync(cancellationToken);
            var dest = await FindFolderAsync(client, MailConstants.FolderContabilidade, cancellationToken)
                       ?? throw new InvalidOperationException("Pasta Contabilidade ausente.");

            var personal = client.GetFolder(client.PersonalNamespaces[0]);
            var folders = new List<IMailFolder> { client.Inbox };
            folders.AddRange(await personal.GetSubfoldersAsync(false, cancellationToken));

            foreach (var folder in folders)
            {
                if (string.Equals(folder.FullName, dest.FullName, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken);
                }
                catch
                {
                    continue;
                }

                var uids = await folder.SearchAsync(SearchQuery.All, cancellationToken);
                if (uids.Count == 0)
                    continue;

                var summaries = await folder.FetchAsync(
                    uids,
                    MessageSummaryItems.Envelope | MessageSummaryItems.UniqueId,
                    cancellationToken);
                var move = new List<UniqueId>();
                foreach (var summary in summaries)
                {
                    var input = ToInput(summary, _settings, true);
                    if (FolderRuleEngine.IsContabilidade(input))
                        move.Add(summary.UniqueId);
                }

                if (move.Count > 0)
                    await folder.MoveToAsync(move, dest, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ApplyRulesUntilEmptyAsync(
        ImapClient client,
        IMailFolder folder,
        Action<IndexedMessage>? onMoved,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken);
            var uids = await folder.SearchAsync(SearchQuery.All, cancellationToken);
            if (uids.Count == 0)
                return;

            var grouped = new Dictionary<string, List<UniqueId>>(StringComparer.OrdinalIgnoreCase);
            var samples = new Dictionary<string, List<IndexedMessage>>(StringComparer.OrdinalIgnoreCase);

            foreach (var batch in uids.Chunk(150))
            {
                var summaries = await folder.FetchAsync(
                    batch.ToList(),
                    MessageSummaryItems.Envelope | MessageSummaryItems.UniqueId | MessageSummaryItems.Flags,
                    cancellationToken);

                foreach (var summary in summaries)
                {
                    var destName = FolderRuleEngine.FolderName(
                        FolderRuleEngine.ResolveFolder(ToInput(summary, _settings, false)));
                    if (destName is null)
                        continue;

                    if (!grouped.TryGetValue(destName, out var list))
                    {
                        list = [];
                        grouped[destName] = list;
                        samples[destName] = [];
                    }

                    list.Add(summary.UniqueId);
                    if (samples[destName].Count < 5)
                        samples[destName].Add(ToIndexed(destName, summary, _settings));
                }
            }

            if (grouped.Count == 0)
                return;

            foreach (var pair in grouped)
            {
                var dest = await FindFolderAsync(client, pair.Key, cancellationToken);
                if (dest is null)
                    continue;

                await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken);
                await folder.MoveToAsync(pair.Value, dest, cancellationToken);
                if (onMoved is null)
                    continue;
                foreach (var message in samples[pair.Key])
                    onMoved(message);
            }
        }
    }

    private async Task MutateFlagsAsync(
        string folderName,
        uint uid,
        MessageFlags flag,
        bool add,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var client = await EnsureConnectedAsync(cancellationToken);
            var folder = await OpenAsync(client, folderName, FolderAccess.ReadWrite, cancellationToken);
            if (add)
                await folder.AddFlagsAsync(new UniqueId(uid), flag, true, cancellationToken);
            else
                await folder.RemoveFlagsAsync(new UniqueId(uid), flag, true, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ImapClient> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_client is { IsAuthenticated: true, IsConnected: true })
            return _client;

        _client?.Dispose();
        _client = new ImapClient { Timeout = 90_000 };
        await _client.ConnectAsync(_settings.ImapHost, _settings.ImapPort, SecureSocketOptions.SslOnConnect, cancellationToken);
        await _client.AuthenticateAsync(_settings.Email, _password, cancellationToken);
        return _client;
    }

    private static async Task<IMailFolder> GetOrCreateFolderAsync(
        IMailFolder parent,
        string name,
        CancellationToken cancellationToken)
    {
        try
        {
            return await parent.GetSubfolderAsync(name, cancellationToken);
        }
        catch
        {
            return await parent.CreateAsync(name, true, cancellationToken);
        }
    }

    private static async Task<IMailFolder> OpenAsync(
        ImapClient client,
        string name,
        FolderAccess access,
        CancellationToken cancellationToken)
    {
        var folder = await FindFolderAsync(client, name, cancellationToken)
                     ?? throw new InvalidOperationException("Pasta não encontrada: " + name);
        await folder.OpenAsync(access, cancellationToken);
        return folder;
    }

    private static async Task<IMailFolder?> FindFolderAsync(
        ImapClient client,
        string name,
        CancellationToken cancellationToken)
    {
        if (name.Equals("INBOX", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Entrada", StringComparison.OrdinalIgnoreCase))
            return client.Inbox;

        try
        {
            if (name.Equals("Sent", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Enviados", StringComparison.OrdinalIgnoreCase))
                return client.GetFolder(SpecialFolder.Sent);
            if (name.Equals("Trash", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Lixeira", StringComparison.OrdinalIgnoreCase))
                return client.GetFolder(SpecialFolder.Trash);
            if (name.Equals("Drafts", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Draft", StringComparison.OrdinalIgnoreCase))
                return client.GetFolder(SpecialFolder.Drafts);
        }
        catch
        {
            // servidor sem pastas especiais
        }

        try
        {
            return await client.GetFolderAsync(name, cancellationToken);
        }
        catch
        {
        }

        var personal = client.GetFolder(client.PersonalNamespaces[0]);
        foreach (var sub in await personal.GetSubfoldersAsync(false, cancellationToken))
        {
            if (sub.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                sub.FullName.Equals(name, StringComparison.OrdinalIgnoreCase))
                return sub;
        }

        try
        {
            foreach (var sub in await client.Inbox.GetSubfoldersAsync(false, cancellationToken))
            {
                if (sub.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return sub;
            }
        }
        catch
        {
        }

        return null;
    }

    private static RuleMatchInput ToInput(IMessageSummary summary, MailSettings settings, bool forceContabilidade)
    {
        var from = summary.Envelope?.From?.Mailboxes.FirstOrDefault();
        return new RuleMatchInput
        {
            From = from?.Address ?? "",
            To = string.Join(";", summary.Envelope?.To?.Mailboxes.Select(m => m.Address) ?? []),
            Cc = string.Join(";", summary.Envelope?.Cc?.Mailboxes.Select(m => m.Address) ?? []),
            Subject = summary.Envelope?.Subject ?? "",
            ContabilidadeSenders = settings.ContabilidadeSenders,
            HasContabilidadeKeyword = forceContabilidade
        };
    }

    private static IndexedMessage ToIndexed(string folder, IMessageSummary summary, MailSettings settings)
    {
        var from = summary.Envelope?.From?.Mailboxes.FirstOrDefault();
        var attachments = summary.Attachments?.Select(a => a.FileName ?? "").ToArray() ?? [];
        var input = ToInput(summary, settings, false);
        var kind = FolderRuleEngine.ResolveFolder(input);
        return new IndexedMessage
        {
            UniqueId = folder + ":" + summary.UniqueId.Id,
            Folder = folder,
            Uid = summary.UniqueId.Id,
            MessageId = summary.Envelope?.MessageId ?? "",
            FromAddress = from?.Address ?? "",
            FromName = from?.Name ?? from?.Address ?? "",
            ToAddresses = string.Join("; ", summary.Envelope?.To?.Mailboxes.Select(m => m.Address) ?? []),
            Subject = summary.Envelope?.Subject ?? "",
            Preview = summary.Envelope?.Subject ?? "",
            DateUtc = (summary.Envelope?.Date ?? DateTimeOffset.Now).UtcDateTime,
            IsSeen = summary.Flags?.HasFlag(MessageFlags.Seen) == true,
            IsFlagged = summary.Flags?.HasFlag(MessageFlags.Flagged) == true,
            HasAttachment = false,
            IsFiscal = FolderRuleEngine.LooksFiscal(summary.Envelope?.Subject, attachments),
            HasUnsubscribe = !string.IsNullOrWhiteSpace(summary.Headers?[HeaderId.ListUnsubscribe]),
            IsContabilidade = FolderRuleEngine.IsContabilidade(input),
            Priority = FolderRuleEngine.ResolvePriority(input, kind)
        };
    }

    public void Dispose()
    {
        _client?.Dispose();
        _gate.Dispose();
    }
}
