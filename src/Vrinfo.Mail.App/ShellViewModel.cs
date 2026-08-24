using System.Collections.ObjectModel;
using System.IO;
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
    private uint? _openDraftComposeUid;
    private readonly HashSet<string> _inboxKeys = new(StringComparer.Ordinal);
    private bool _suppressToasts;
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
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isOpeningCompose;
    [ObservableProperty] private bool startWithWindows = true;
    [ObservableProperty] private bool isDarkTheme;

    public string ThemeToggleLabel => IsDarkTheme ? "Modo claro" : "Modo escuro";

    public event Action<string, string>? ToastRequested;
    public event Action<string>? HtmlChanged;

    public async Task StartAsync()
    {
        _settings = _store.Load();
        StartWithWindows = _settings.StartWithWindows;
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
            _settings.StartWithWindows = true;
            StartWithWindows = true;
            _store.Save(_settings);
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
        Folders.Add(new FolderNavItem { Id = MailConstants.FolderInovafarma, Title = "Inovafarma", Logo = LoadLogo("inovafarma.png") });
        Folders.Add(new FolderNavItem { Id = MailConstants.FolderHiper, Title = "Hiper", Logo = LoadLogo("hiper.png") });
        Folders.Add(new FolderNavItem { Id = MailConstants.FolderContas, Title = "Contas", Logo = LoadLogo("contas.png") });
        Folders.Add(new FolderNavItem { Id = MailConstants.FolderContabilidade, Title = "Contabilidade", Logo = LoadLogo("contabilidade.png") });
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
        try
        {
            _suppressToasts = true;
            Status = "Aplicando filtro inteligente…";
            await _mailbox.ApplyMailboxSmartRulesAsync(null, _cts.Token);
            Status = "Atualizando pastas…";
            await _mailbox.MarkMailboxReadAsync(_cts.Token);
            await SyncKnownFoldersAsync();
            CaptureInboxKeys();
            _suppressToasts = false;
            Status = "Caixa sincronizada · IDLE ativo";
        }
        catch (Exception ex)
        {
            _suppressToasts = false;
            Status = "Sincronização em segundo plano: " + ex.Message;
        }
    }

    private void RefreshUnread()
    {
        foreach (var folder in Folders)
            folder.Unread = _index.UnreadCount(folder.Id);
    }

    public void ReloadList()
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

    partial void OnSelectedMessageChanged(IndexedMessage? value) => _ = OpenMessageAsync(value);

    private async Task SyncKnownFoldersAsync()
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

        RefreshUnread();
        ReloadList();
    }

    private async Task RefreshFromIdleAsync()
    {
        try
        {
            await _mailbox.ApplyInboxRulesAsync(_suppressToasts ? null : OnMoved, _cts.Token);
            var inbox = await _mailbox.SyncFolderAsync("INBOX", _cts.Token);
            _index.ReplaceFolder("INBOX", inbox);
            var newcomers = inbox.Where(m => !_inboxKeys.Contains(m.UniqueId)).ToList();
            CaptureInboxKeys(inbox);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                RefreshUnread();
                ReloadList();
                Status = "Novas mensagens · " + DateTime.Now.ToString("HH:mm");
                foreach (var item in newcomers.Take(3))
                    ToastRequested?.Invoke(item.DisplayFrom, item.Subject);
            });
        }
        catch
        {
            // o loop IDLE tenta de novo
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

    private async Task OpenMessageAsync(IndexedMessage? message)
    {
        if (message is null)
            return;
        var generation = Interlocked.Increment(ref _openGeneration);
        try
        {
            _openMime = await _mailbox.GetMessageAsync(message.Folder, message.Uid, _cts.Token);
            if (generation != _openGeneration)
                return;
            SubjectLine = _openMime.Subject ?? message.Subject;
            FromLine = _openMime.From.ToString();
            HtmlBody = string.IsNullOrWhiteSpace(_openMime.HtmlBody)
                ? "<pre style='font-family:Segoe UI;white-space:pre-wrap'>" + System.Net.WebUtility.HtmlEncode(_openMime.TextBody ?? "") + "</pre>"
                : _openMime.HtmlBody;
            HtmlChanged?.Invoke(HtmlBody);
            if (IsDraftsFolder(message.Folder))
            {
                if (_openDraftComposeUid != message.Uid)
                {
                    _openDraftComposeUid = message.Uid;
                    OpenCompose(_openMime, false, message.Uid, loadAsDraft: true);
                }
                return;
            }
            if (!message.IsSeen)
            {
                await _mailbox.SetSeenAsync(message.Folder, message.Uid, true, _cts.Token);
                message.IsSeen = true;
                _index.Upsert(message);
                RefreshUnread();
            }
        }
        catch (Exception ex)
        {
            Status = "Não foi possível abrir: " + ex.Message;
        }
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

    partial void OnIsOpeningComposeChanged(bool value) => ComposeCommand.NotifyCanExecuteChanged();

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
        if (_openMime is null) return;
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Salvar anexos em" };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;
        foreach (var att in _openMime.Attachments.OfType<MimePart>())
        {
            var name = att.FileName ?? "anexo.bin";
            await using var stream = File.Create(Path.Combine(dialog.SelectedPath, name));
            att.Content.DecodeTo(stream);
        }
        Status = "Anexos salvos.";
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
        _cts.Dispose();
    }
}
