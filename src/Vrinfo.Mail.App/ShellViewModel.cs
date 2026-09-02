using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MimeKit;
using Vrinfo.Mail.Core;
using Vrinfo.Mail.Imap;

namespace Vrinfo.Mail.App;

public sealed class FolderNavItem : ObservableObject
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required ImageSource Logo { get; init; }

    private int _unread;
    public int Unread
    {
        get => _unread;
        set => SetProperty(ref _unread, value);
    }
}

public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly MailSettingsStore _store = new();
    private readonly SqliteMessageIndex _index = new();
    private readonly ImapMailbox _mailbox = new();
    private readonly ImapIdleHost _idle = new();
    private MailSettings _settings = new();
    private string _password = string.Empty;
    private readonly CancellationTokenSource _cts = new();
    private MimeMessage? _openMime;
    private MessageQuickView? _openView;
    private uint? _openDraftComposeUid;
    private readonly HashSet<string> _inboxKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, MessageQuickView> _newBodyCache = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _cacheOrder = new();
    private long _cacheBytes;
    private const long BodyCacheLimitBytes = 50L * 1024 * 1024;
    private readonly SemaphoreSlim _folderSyncGate = new(1, 1);
    private bool _suppressToasts;
    private bool _suppressMessageOpen;
    private int _openGeneration;
    private CancellationTokenSource? _searchCts;

    public ObservableCollection<FolderNavItem> Folders { get; } = [];
    public ObservableCollection<IndexedMessage> Messages { get; } = [];

    [ObservableProperty] private FolderNavItem? selectedFolder;
    [ObservableProperty] private IndexedMessage? selectedMessage;
    [ObservableProperty] private string search = string.Empty;
    [ObservableProperty] private bool unreadOnly;
    [ObservableProperty] private bool attachmentsOnly;
    [ObservableProperty] private bool todayOnly;
    [ObservableProperty] private bool highPriorityOnly;
    [ObservableProperty] private bool fiscalOnly;
    [ObservableProperty] private string status = "Conectando…";
    [ObservableProperty] private string htmlBody = "<p></p>";
    [ObservableProperty] private string subjectLine = "";
    [ObservableProperty] private string fromLine = "";
    [ObservableProperty] private string receivedLine = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isOpeningCompose;
    [ObservableProperty] private bool isOpeningMessage;
    [ObservableProperty] private bool hasOpenAttachments;
    [ObservableProperty] private double openingProgress;
    [ObservableProperty] private bool foldersExpanded = true;
    [ObservableProperty] private bool startWithWindows = true;
    [ObservableProperty] private bool enableTray = true;
    [ObservableProperty] private bool minimizeToTray = true;
    [ObservableProperty] private bool isDarkTheme;

    public string ThemeToggleLabel => IsDarkTheme ? "Modo claro" : "Modo escuro";
    public string ThemeIcon => IsDarkTheme ? "☀" : "☽";
    public string ComposeButtonLabel => IsOpeningCompose ? "Abrindo…" : "Enviar E-mail";
    public GridLength FolderColumnWidth => FoldersExpanded ? new GridLength(176) : new GridLength(52);
    public string FolderToggleGlyph => FoldersExpanded ? "‹" : "›";
    public string FolderToggleHint => FoldersExpanded ? "Reduzir pastas" : "Expandir pastas";
    public IEnumerable<FolderNavItem> SenderRuleFolders => Folders.Where(f => RuleDestinationFolders.Contains(f.Id, StringComparer.OrdinalIgnoreCase));

    public event Action<string, string>? ToastRequested;
    public event Action<string>? HtmlChanged;

    public async Task StartAsync()
    {
        _settings = _store.Load();
        FoldersExpanded = _settings.FoldersExpanded;
        StartWithWindows = _settings.StartWithWindows;
        EnableTray = _settings.EnableTray;
        MinimizeToTray = _settings.MinimizeToTray;
        IsDarkTheme = _settings.UseDarkTheme;
        Theme.Apply(IsDarkTheme);
        _password = WindowsCredentialStore.Read(_settings.Email) ?? "";
        BuildFolders();
        SelectedFolder = Folders.FirstOrDefault();
        ReloadList();

        if (string.IsNullOrWhiteSpace(_password) || !EmailAddressHelper.IsValid(_settings.Email))
        {
            Status = "Configure a conta para sincronizar.";
            return;
        }

        try
        {
            IsBusy = true;
            Status = "Conectando IMAP…";
            await _mailbox.ConnectAsync(_settings, _password, _cts.Token);
            Status = "Sincronizando Entrada…";
            try
            {
                var inbox = await _mailbox.SyncFolderAsync("INBOX", _cts.Token);
                _index.ReplaceFolder("INBOX", inbox);
            }
            catch
            {
                // continua o restante em segundo plano
            }
            CaptureInboxKeys();
            RefreshUnread();
            ReloadList();
            _suppressToasts = false;
            _idle.InboxChanged += (_, _) => _ = RefreshFromIdleAsync();
            _idle.Start(_settings, _password);
            IsBusy = false;
            Status = "Pronto · sincronizando pastas em segundo plano…";
            ApplyAutostart();
            _ = FinishStartupAsync();
        }
        catch (Exception ex)
        {
            Status = "Falha IMAP: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void BuildFolders()
    {
        Folders.Clear();
        Folders.Add(new FolderNavItem { Id = "INBOX", Title = "Entrada", Logo = LoadLogo("inbox.png") });
        if (_settings.FolderInovafarmaEnabled)
            Folders.Add(new FolderNavItem { Id = MailConstants.FolderInovafarma, Title = "Inovafarma", Logo = LoadLogo("inovafarma.png") });
        if (_settings.FolderHiperEnabled)
            Folders.Add(new FolderNavItem { Id = MailConstants.FolderHiper, Title = "Hiper", Logo = LoadLogo("hiper.png") });
        if (_settings.FolderContasEnabled)
            Folders.Add(new FolderNavItem { Id = MailConstants.FolderContas, Title = "Contas", Logo = LoadLogo("contas.png") });
        if (_settings.FolderContabilidadeEnabled)
            Folders.Add(new FolderNavItem { Id = MailConstants.FolderContabilidade, Title = "Contabilidade", Logo = LoadLogo("contabilidade.png") });
        if (_settings.FolderDiscordEnabled)
            Folders.Add(new FolderNavItem { Id = MailConstants.FolderDiscord, Title = "Discord", Logo = LoadLogo("discord.png") });
        Folders.Add(new FolderNavItem { Id = MailConstants.FolderDrafts, Title = "Rascunhos", Logo = LoadLogo("drafts.png") });
        Folders.Add(new FolderNavItem { Id = "Sent", Title = "Enviados", Logo = LoadLogo("sent.png") });
        Folders.Add(new FolderNavItem { Id = "Trash", Title = "Lixeira", Logo = LoadLogo("trash.png") });
        RefreshUnread();
    }

    private static ImageSource LoadLogo(string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Logos", file);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bitmap.DecodePixelWidth = 96;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private async Task FinishStartupAsync()
    {
        await _folderSyncGate.WaitAsync(_cts.Token);
        try
        {
            _suppressToasts = true;
            Status = "Aplicando filtro inteligente…";
            await _mailbox.ApplyMailboxSmartRulesAsync(null, _cts.Token);
            Status = "Atualizando pastas…";
            await SyncKnownFoldersCoreAsync();
            CaptureInboxKeys();
            _suppressToasts = false;
            Status = "Caixa sincronizada · IDLE ativo";
        }
        catch (Exception ex)
        {
            _suppressToasts = false;
            Status = "Sincronização em segundo plano: " + ex.Message;
        }
        finally
        {
            _folderSyncGate.Release();
        }
    }

    private void RefreshUnread()
    {
        foreach (var folder in Folders)
            folder.Unread = _index.UnreadCount(folder.Id);
    }

    public void ReloadList()
    {
        var keepFolder = SelectedMessage?.Folder;
        var keepUid = SelectedMessage?.Uid;
        _suppressMessageOpen = true;
        try
        {
            Messages.Clear();
            foreach (var item in _index.Query(
                         SelectedFolder?.Id,
                         Search,
                         UnreadOnly,
                         AttachmentsOnly,
                         TodayOnly,
                         HighPriorityOnly,
                         FiscalOnly))
                Messages.Add(item);
            if (keepFolder is not null && keepUid is uint uid)
                SelectedMessage = Messages.FirstOrDefault(m => m.Folder == keepFolder && m.Uid == uid);
        }
        finally
        {
            _suppressMessageOpen = false;
        }
    }

    partial void OnSelectedFolderChanged(FolderNavItem? value) => ReloadList();
    partial void OnSearchChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        _ = ReloadListDebouncedAsync(token);
    }
    private async Task ReloadListDebouncedAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(160, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        ReloadList();
    }
    partial void OnUnreadOnlyChanged(bool value) => ReloadList();
    partial void OnAttachmentsOnlyChanged(bool value) => ReloadList();
    partial void OnTodayOnlyChanged(bool value) => ReloadList();
    partial void OnHighPriorityOnlyChanged(bool value) => ReloadList();
    partial void OnFiscalOnlyChanged(bool value) => ReloadList();

    partial void OnSelectedMessageChanged(IndexedMessage? value)
    {
        if (_suppressMessageOpen)
            return;
        _ = OpenMessageAsync(value);
    }

    public void OpenFromList(IndexedMessage message)
    {
        if (!ReferenceEquals(SelectedMessage, message))
            SelectedMessage = message;
        else
            _ = OpenMessageAsync(message);
    }

    public void SelectMessageForContext(IndexedMessage message)
    {
        _suppressMessageOpen = true;
        try
        {
            SelectedMessage = message;
        }
        finally
        {
            _suppressMessageOpen = false;
        }
    }

    private async Task SyncKnownFoldersAsync()
    {
        await _folderSyncGate.WaitAsync(_cts.Token);
        try
        {
            await SyncKnownFoldersCoreAsync();
        }
        finally
        {
            _folderSyncGate.Release();
        }
    }

    private async Task SyncKnownFoldersCoreAsync()
    {
        foreach (var folder in new[]
                 {
                     "INBOX", "Sent", "Trash",
                     MailConstants.FolderInovafarma,
                     MailConstants.FolderHiper,
                     MailConstants.FolderContas,
                     MailConstants.FolderContabilidade,
                     MailConstants.FolderDiscord,
                     MailConstants.FolderDrafts
                 })
        {
            try
            {
                var items = await _mailbox.SyncFolderAsync(folder, _cts.Token);
                if (folder == MailConstants.FolderDrafts)
                {
                    foreach (var item in items)
                        item.Folder = MailConstants.FolderDrafts;
                }
                _index.ReplaceFolder(folder, items);
            }
            catch
            {
                // pasta pode não existir neste servidor
            }
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;
        await dispatcher.InvokeAsync(() =>
        {
            RefreshUnread();
            ReloadList();
        });
    }

    private static readonly string[] RuleDestinationFolders =
    [
        MailConstants.FolderInovafarma,
        MailConstants.FolderHiper,
        MailConstants.FolderContas,
        MailConstants.FolderContabilidade,
        MailConstants.FolderDiscord
    ];

    private async Task RefreshFromIdleAsync()
    {
        await _folderSyncGate.WaitAsync(_cts.Token);
        try
        {
            var moved = new List<IndexedMessage>();
            await _mailbox.ApplyInboxRulesAsync(message =>
            {
                moved.Add(message);
                OnMoved(message);
            }, _cts.Token);
            var inbox = await _mailbox.SyncFolderAsync("INBOX", _cts.Token);
            _index.ReplaceFolder("INBOX", inbox);
            foreach (var folder in RuleDestinationFolders)
            {
                try
                {
                    var items = await _mailbox.SyncFolderAsync(folder, _cts.Token);
                    _index.ReplaceFolder(folder, items);
                }
                catch
                {
                    // pasta pode não existir neste servidor
                }
            }

            var newcomers = inbox.Where(m => !_inboxKeys.Contains(m.UniqueId)).ToList();
            CaptureInboxKeys(inbox);
            PrefetchNewBodies(newcomers.Concat(moved));
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                RefreshUnread();
                ReloadList();
                if (newcomers.Count == 0)
                    return;
                Status = "Novas mensagens · " + DateTime.Now.ToString("HH:mm");
                if (_suppressToasts)
                    return;
                foreach (var item in newcomers.Take(3))
                    ToastRequested?.Invoke(item.DisplayFrom, item.Subject);
            });
        }
        catch
        {
            // o loop IDLE tenta de novo
        }
        finally
        {
            _folderSyncGate.Release();
        }
    }

    private void CaptureInboxKeys(IEnumerable<IndexedMessage>? inbox = null)
    {
        _inboxKeys.Clear();
        foreach (var item in inbox ?? _index.Query("INBOX", null, false, false, false, false, false))
            _inboxKeys.Add(item.UniqueId);
    }

    private void OnMoved(IndexedMessage message)
    {
        if (_suppressToasts)
            return;
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            ToastRequested?.Invoke(message.DisplayFrom, message.Subject);
        });
    }

    private static string BodyCacheKey(string folder, uint uid) => folder + "\0" + uid.ToString();

    private IndexedMessage ResolveCachedTarget(IndexedMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.MessageId))
            return message;
        var match = _index.Query(message.Folder, null, false, false, false, false, false)
            .FirstOrDefault(m => string.Equals(m.MessageId, message.MessageId, StringComparison.OrdinalIgnoreCase));
        return match ?? message;
    }

    private void PrefetchNewBodies(IEnumerable<IndexedMessage> items)
    {
        foreach (var raw in items.Take(12))
        {
            var message = ResolveCachedTarget(raw);
            if (IsDraftsFolder(message.Folder))
                continue;
            _ = PrefetchOneAsync(message);
        }
    }

    private async Task PrefetchOneAsync(IndexedMessage message)
    {
        var key = BodyCacheKey(message.Folder, message.Uid);
        if (_newBodyCache.ContainsKey(key) || Volatile.Read(ref _cacheBytes) >= BodyCacheLimitBytes)
            return;
        try
        {
            for (var i = 0; i < 40 && IsOpeningMessage; i++)
                await Task.Delay(50, _cts.Token);
            if (IsOpeningMessage)
                return;
            var view = await _mailbox.GetQuickViewAsync(message.Folder, message.Uid, null, _cts.Token, useBackgroundSession: true);
            TryAddCache(key, view);
        }
        catch
        {
            // abre sob demanda
        }
    }

    private void TryAddCache(string key, MessageQuickView view)
    {
        var size = view.ByteSize;
        if (size <= 0 || size > BodyCacheLimitBytes)
            return;
        while (Volatile.Read(ref _cacheBytes) + size > BodyCacheLimitBytes && _cacheOrder.TryDequeue(out var oldKey))
        {
            if (_newBodyCache.TryRemove(oldKey, out var old) && oldKey != key)
                Interlocked.Add(ref _cacheBytes, -old.ByteSize);
        }

        if (!_newBodyCache.TryAdd(key, view))
            return;
        Interlocked.Add(ref _cacheBytes, size);
        _cacheOrder.Enqueue(key);
    }

    private async Task PersistSeenAsync(IndexedMessage message)
    {
        try
        {
            await _mailbox.SetSeenAsync(message.Folder, message.Uid, true, _cts.Token);
        }
        catch (Exception ex)
        {
            Status = "Não foi possível marcar como lido: " + ex.Message;
        }
    }

    private async Task OpenMessageAsync(IndexedMessage? message)
    {
        if (message is null)
            return;
        var generation = Interlocked.Increment(ref _openGeneration);
        SubjectLine = message.Subject;
        FromLine = message.DisplayFrom;
        ReceivedLine = "Recebido em " + message.DateUtc.ToLocalTime().ToString("dd/MM/yyyy 'às' HH:mm");
        HasOpenAttachments = message.HasAttachment;
        HtmlBody = string.IsNullOrWhiteSpace(message.Preview)
            ? "<p style='opacity:.65'>Carregando mensagem…</p>"
            : "<p>" + System.Net.WebUtility.HtmlEncode(message.Preview) + "</p>";
        HtmlChanged?.Invoke(HtmlBody);
        IsOpeningMessage = true;
        OpeningProgress = 10;

        if (!message.IsSeen && !IsDraftsFolder(message.Folder))
        {
            message.IsSeen = true;
            _index.Upsert(message);
            RefreshUnread();
            if (UnreadOnly)
                ReloadList();
            _ = PersistSeenAsync(message);
        }

        try
        {
            MessageQuickView view;
            var cacheKey = BodyCacheKey(message.Folder, message.Uid);
            if (IsDraftsFolder(message.Folder))
            {
                OpeningProgress = 40;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(18));
                var mime = await _mailbox.GetMessageAsync(message.Folder, message.Uid, timeout.Token);
                if (generation != _openGeneration)
                    return;
                view = new MessageQuickView
                {
                    Folder = message.Folder,
                    Uid = message.Uid,
                    Subject = mime.Subject ?? message.Subject,
                    From = mime.From.ToString(),
                    Html = string.IsNullOrWhiteSpace(mime.HtmlBody)
                        ? "<pre style='font-family:Segoe UI;white-space:pre-wrap'>" + System.Net.WebUtility.HtmlEncode(mime.TextBody ?? "") + "</pre>"
                        : mime.HtmlBody,
                    Text = mime.TextBody ?? "",
                    Mime = mime
                };
            }
            else if (_newBodyCache.TryGetValue(cacheKey, out var cached))
            {
                OpeningProgress = 70;
                view = cached;
            }
            else
            {
                var progress = new Progress<int>(value =>
                {
                    if (generation == _openGeneration)
                        OpeningProgress = value;
                });
                view = await _mailbox.GetQuickViewAsync(message.Folder, message.Uid, progress, _cts.Token);
                TryAddCache(cacheKey, view);
            }

            if (generation != _openGeneration)
                return;

            _openMime = view.Mime;
            _openView = view;
            HasOpenAttachments = view.Parts.Any(p => !p.IsInline || !p.IsImage) || view.Mime.Attachments.Any();
            OpeningProgress = 82;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                SubjectLine = view.Subject;
                FromLine = view.From;
                HtmlBody = view.Html;
                HtmlChanged?.Invoke(HtmlBody);
            });
            IsOpeningMessage = false;

            if (IsDraftsFolder(message.Folder))
            {
                IsOpeningMessage = false;
                if (_openDraftComposeUid != message.Uid)
                {
                    _openDraftComposeUid = message.Uid;
                    OpenCompose(_openMime, false, message.Uid, loadAsDraft: true);
                }
                return;
            }

            OpeningProgress = 90;
            await LoadInlineImagesAsync(view, generation);
            OpeningProgress = 100;
        }
        catch (OperationCanceledException)
        {
            if (generation == _openGeneration)
                Status = "Abertura interrompida.";
        }
        catch (Exception ex)
        {
            if (generation == _openGeneration)
                Status = "Não foi possível abrir: " + ex.Message;
        }
        finally
        {
            if (generation == _openGeneration)
                IsOpeningMessage = false;
        }
    }

    private async Task LoadInlineImagesAsync(MessageQuickView view, int generation)
    {
        var html = view.Html;
        var changed = false;
        foreach (var part in view.Parts.Where(p => p.IsImage && p.IsInline && p.ContentId.Length > 0))
        {
            if (generation != _openGeneration)
                return;
            if (part.Octets > 2_500_000)
                continue;
            try
            {
                var bytes = await _mailbox.GetPartBytesAsync(view.Folder, view.Uid, part.Specifier, _cts.Token);
                if (bytes is null || bytes.Length == 0)
                    continue;
                var data = "data:" + part.ContentType + ";base64," + Convert.ToBase64String(bytes);
                var next = ReplaceCid(html, part.ContentId, data);
                if (next == html)
                    continue;
                html = next;
                changed = true;
            }
            catch
            {
                // imagem segue lazy; o texto já está visível
            }
        }

        if (!changed || generation != _openGeneration)
            return;
        view.Html = html;
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            HtmlBody = html;
            HtmlChanged?.Invoke(HtmlBody);
        });
    }

    private static string ReplaceCid(string html, string contentId, string dataUri)
    {
        var id = contentId.Trim();
        if (id.Length == 0)
            return html;
        html = html.Replace("cid:" + id, dataUri, StringComparison.OrdinalIgnoreCase);
        html = html.Replace("CID:" + id, dataUri, StringComparison.OrdinalIgnoreCase);
        return html;
    }

    [RelayCommand]
    private async Task Refresh()
    {
        try
        {
            IsBusy = true;
            await _mailbox.ApplyMailboxSmartRulesAsync(OnMoved, _cts.Token);
            await SyncKnownFoldersAsync();
            Status = "Atualizado " + DateTime.Now.ToString("HH:mm");
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SetSelectedSeenAsync(bool seen)
    {
        if (SelectedMessage is null)
            return;
        var message = SelectedMessage;
        await _mailbox.SetSeenAsync(message.Folder, message.Uid, seen, _cts.Token);
        message.IsSeen = seen;
        _index.Upsert(message);
        RefreshUnread();
        if (UnreadOnly && seen)
            ReloadList();
    }

    public async Task DeleteSelectedFromContextAsync() => await DeleteSelected();

    public async Task MarkSelectedFolderReadAsync() => await MarkFolderRead();

    public async Task AddSenderFolderRuleAsync(FolderNavItem destination)
    {
        if (SelectedMessage is null || string.IsNullOrWhiteSpace(SelectedMessage.FromAddress))
            return;
        var message = SelectedMessage;
        _settings.SenderFolderRules[message.FromAddress.Trim()] = destination.Id;
        _store.Save(_settings);
        if (!string.Equals(message.Folder, destination.Id, StringComparison.OrdinalIgnoreCase))
        {
            await _mailbox.MoveAsync(message.Folder, message.Uid, destination.Id, _cts.Token);
            _index.DeleteByUniqueId(message.UniqueId);
            ReloadList();
            await SyncKnownFoldersAsync();
        }
        Status = $"Regra criada: {message.FromAddress} → {destination.Title}.";
    }

    [RelayCommand]
    private async Task DeleteSelected()
    {
        if (SelectedMessage is null) return;
        await _mailbox.DeleteAsync(SelectedMessage.Folder, SelectedMessage.Uid, _cts.Token);
        _index.DeleteByUniqueId(SelectedMessage.UniqueId);
        ReloadList();
    }

    [RelayCommand]
    private async Task EmptyTrash()
    {
        await _mailbox.EmptyTrashAsync(_cts.Token);
        await SyncKnownFoldersAsync();
    }

    [RelayCommand]
    private async Task MarkFolderRead()
    {
        if (SelectedFolder is null) return;
        await _mailbox.MarkFolderReadAsync(SelectedFolder.Id, _cts.Token);
        await SyncKnownFoldersAsync();
    }

    [RelayCommand]
    private async Task TagContabilidade()
    {
        if (SelectedMessage is null) return;
        var sender = SelectedMessage.FromAddress;
        if (!string.IsNullOrWhiteSpace(sender) &&
            !_settings.ContabilidadeSenders.Contains(sender, StringComparer.OrdinalIgnoreCase))
        {
            _settings.ContabilidadeSenders.Add(sender);
            _store.Save(_settings);
        }

        await _mailbox.AddContabilidadeKeywordAsync(SelectedMessage.Folder, SelectedMessage.Uid, _cts.Token);
        await _mailbox.MoveAsync(SelectedMessage.Folder, SelectedMessage.Uid, MailConstants.FolderContabilidade, _cts.Token);
        Status = "Aplicando tag Contabilidade no histórico…";
        await _mailbox.ApplyContabilidadeRetroactiveAsync(_cts.Token);
        await SyncKnownFoldersAsync();
        Status = "Contabilidade atualizada (passados e futuros).";
    }

    [RelayCommand]
    private async Task MoveToInovafarma()
    {
        if (SelectedMessage is null) return;
        await _mailbox.MoveAsync(SelectedMessage.Folder, SelectedMessage.Uid, MailConstants.FolderInovafarma, _cts.Token);
        await SyncKnownFoldersAsync();
    }

    [RelayCommand]
    private async Task MoveToHiper()
    {
        if (SelectedMessage is null) return;
        await _mailbox.MoveAsync(SelectedMessage.Folder, SelectedMessage.Uid, MailConstants.FolderHiper, _cts.Token);
        await SyncKnownFoldersAsync();
    }

    [RelayCommand]
    private async Task CleanupNewsletters()
    {
        if (SelectedFolder is null) return;
        await _mailbox.CleanupNewslettersAsync(SelectedFolder.Id, 30, _cts.Token);
        await SyncKnownFoldersAsync();
        Status = "Newsletters com mais de 30 dias enviadas à lixeira.";
    }

    [RelayCommand]
    private async Task ToggleFlag()
    {
        if (SelectedMessage is null) return;
        var next = !SelectedMessage.IsFlagged;
        await _mailbox.SetFlaggedAsync(SelectedMessage.Folder, SelectedMessage.Uid, next, _cts.Token);
        SelectedMessage.IsFlagged = next;
        SelectedMessage.Priority = next ? MessagePriorityLevel.High : MessagePriorityLevel.Normal;
        _index.Upsert(SelectedMessage);
        ReloadList();
    }

    partial void OnIsOpeningComposeChanged(bool value)
    {
        ComposeCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ComposeButtonLabel));
    }

    private bool CanCompose() => !IsOpeningCompose;

    [RelayCommand(CanExecute = nameof(CanCompose))]
    private async Task ComposeAsync()
    {
        IsOpeningCompose = true;
        try
        {
            var dispatcher = System.Windows.Application.Current.Dispatcher;
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Delay(40);
            await OpenComposeAsync(null, false);
        }
        finally
        {
            IsOpeningCompose = false;
        }
    }

    [RelayCommand]
    private void Reply()
    {
        if (_openMime is null) return;
        OpenCompose(_openMime, false);
    }

    [RelayCommand]
    private void Forward()
    {
        if (_openMime is null) return;
        OpenCompose(_openMime, true);
    }

    private void OpenCompose(MimeMessage? original, bool forward, uint? draftUid = null, bool loadAsDraft = false)
        => _ = OpenComposeAsync(original, forward, draftUid, loadAsDraft);

    private async Task OpenComposeAsync(MimeMessage? original, bool forward, uint? draftUid = null, bool loadAsDraft = false)
    {
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        await dispatcher.InvokeAsync(() =>
        {
            var window = new ComposeWindow(
                _settings,
                _password,
                original,
                forward,
                _mailbox,
                () => _ = RefreshDraftsAsync(),
                draftUid,
                loadAsDraft);
            window.Loaded += (_, _) => opened.TrySetResult();
            window.Closed += (_, _) =>
            {
                if (draftUid is uint uid && _openDraftComposeUid == uid)
                    _openDraftComposeUid = null;
                _ = RefreshDraftsAsync();
            };
            window.Show();
            window.Activate();
        });

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        timeout.Token.Register(() => opened.TrySetResult());
        await opened.Task;
    }

    private static bool IsDraftsFolder(string folder)
        => folder.Equals(MailConstants.FolderDrafts, StringComparison.OrdinalIgnoreCase)
           || folder.Contains("Rascunho", StringComparison.OrdinalIgnoreCase);

    private async Task RefreshDraftsAsync()
    {
        try
        {
            var items = await _mailbox.SyncFolderAsync(MailConstants.FolderDrafts, _cts.Token);
            foreach (var item in items)
                item.Folder = MailConstants.FolderDrafts;
            _index.ReplaceFolder(MailConstants.FolderDrafts, items);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                RefreshUnread();
                ReloadList();
            });
        }
        catch
        {
            // pasta ainda não existe
        }
    }

    [RelayCommand]
    private async Task SaveAttachment()
    {
        if (_openView is null && _openMime is null)
            return;
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Salvar anexos em" };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        var saved = 0;
        if (_openView is not null)
        {
            foreach (var part in _openView.Parts.Where(p => !string.IsNullOrWhiteSpace(p.Specifier) && (!p.IsInline || !p.IsImage)))
            {
                var bytes = await _mailbox.GetPartBytesAsync(_openView.Folder, _openView.Uid, part.Specifier, _cts.Token);
                if (bytes is null || bytes.Length == 0)
                    continue;
                var name = string.IsNullOrWhiteSpace(part.FileName) ? "anexo.bin" : part.FileName;
                await File.WriteAllBytesAsync(Path.Combine(dialog.SelectedPath, name), bytes);
                saved++;
            }
        }

        if (saved == 0 && _openMime is not null)
        {
            foreach (var att in _openMime.Attachments.OfType<MimePart>())
            {
                var name = att.FileName ?? "anexo.bin";
                await using var stream = File.Create(Path.Combine(dialog.SelectedPath, name));
                att.Content.DecodeTo(stream);
                saved++;
            }
        }

        Status = saved == 0 ? "Esta mensagem não tem anexo para salvar." : "Anexos salvos.";
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var owner = System.Windows.Application.Current?.MainWindow;
        var window = new SettingsWindow
        {
            Owner = owner,
            DataContext = new SettingsViewModel(_store, _settings, _settings.Email)
        };
        window.ShowDialog();
    }

    public async Task ApplySettingsAsync(MailSettings settings, string? newPassword)
    {
        _settings = settings;
        StartWithWindows = settings.StartWithWindows;
        EnableTray = settings.EnableTray;
        MinimizeToTray = settings.MinimizeToTray;
        if (!string.IsNullOrWhiteSpace(newPassword))
            _password = newPassword;
        else if (EmailAddressHelper.IsValid(settings.Email))
            _password = WindowsCredentialStore.Read(settings.Email) ?? _password;

        ApplyAutostart();
        (System.Windows.Application.Current as App)?.ApplyTray(settings.EnableTray);
        var selectedId = SelectedFolder?.Id;
        BuildFolders();
        SelectedFolder = Folders.FirstOrDefault(f => f.Id == selectedId) ?? Folders.FirstOrDefault();
        ReloadList();

        if (string.IsNullOrWhiteSpace(_password) || !EmailAddressHelper.IsValid(settings.Email))
        {
            Status = "Conta salva. Informe a senha para sincronizar.";
            return;
        }

        try
        {
            IsBusy = true;
            Status = "Reconectando…";
            await _mailbox.ConnectAsync(_settings, _password, _cts.Token);
            _idle.Start(_settings, _password);
            Status = "Configurações aplicadas.";
        }
        catch (Exception ex)
        {
            Status = "Configurações salvas, falha ao reconectar: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ToggleFolders()
    {
        FoldersExpanded = !FoldersExpanded;
    }

    partial void OnFoldersExpandedChanged(bool value)
    {
        _settings.FoldersExpanded = value;
        _store.Save(_settings);
        OnPropertyChanged(nameof(FolderColumnWidth));
        OnPropertyChanged(nameof(FolderToggleGlyph));
        OnPropertyChanged(nameof(FolderToggleHint));
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        _settings.UseDarkTheme = value;
        _store.Save(_settings);
        Theme.Apply(value);
        OnPropertyChanged(nameof(ThemeToggleLabel));
        OnPropertyChanged(nameof(ThemeIcon));
        HtmlChanged?.Invoke(HtmlBody);
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        _settings.StartWithWindows = value;
        _store.Save(_settings);
        ApplyAutostart();
    }

    private void ApplyAutostart()
    {
        try
        {
            var exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "VRINFO.Mail.exe");
            WindowsAutostart.SetEnabled(_settings.StartWithWindows, exe);
        }
        catch
        {
            // schtasks pode exigir permissão; o app continua
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _idle.Dispose();
        _mailbox.Dispose();
        _index.Dispose();
        _folderSyncGate.Dispose();
        _cts.Dispose();
    }
}
