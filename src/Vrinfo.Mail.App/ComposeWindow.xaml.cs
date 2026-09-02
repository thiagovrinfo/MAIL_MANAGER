using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MimeKit;
using Vrinfo.Mail.Core;
using Vrinfo.Mail.Imap;
using MediaColor = System.Windows.Media.Color;
using WpfImage = System.Windows.Controls.Image;
using WpfFontFamily = System.Windows.Media.FontFamily;

namespace Vrinfo.Mail.App;

public partial class ComposeWindow : System.Windows.Window
{
    private static readonly string[] MailFonts =
    [
        "Calibri", "Arial", "Georgia", "Tahoma", "Verdana",
        "Times New Roman", "Courier New", "Trebuchet MS", "Segoe UI"
    ];

    private static readonly double[] MailSizes = [10, 11, 12, 14, 16, 18, 20, 24, 28, 36, 48, 60, 72, 96, 120, 150];

    private readonly MailSettings _settings;
    private readonly string _password;
    private readonly MimeMessage? _original;
    private readonly bool _forward;
    private readonly ImapMailbox? _mailbox;
    private readonly Action? _onDraftsChanged;
    private readonly bool _loadAsDraft;
    private readonly ObservableCollection<string> _attachments = [];
    private readonly string? _signaturePath;
    private readonly DispatcherTimer _draftTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    private readonly SemaphoreSlim _draftLock = new(1, 1);
    private uint? _draftUid;
    private bool _sent;
    private bool _discarded;
    private bool _allowClose;
    private bool _closingInProgress;
    private bool _draftDirty;
    private bool _suspendDirty = true;

    public ComposeWindow(
        MailSettings settings,
        string password,
        MimeMessage? original,
        bool forward,
        ImapMailbox? mailbox = null,
        Action? onDraftsChanged = null,
        uint? existingDraftUid = null,
        bool loadAsDraft = false)
    {
        InitializeComponent();
        AppIcon.ApplyTo(this);
        _settings = settings;
        _password = password;
        _original = original;
        _forward = forward;
        _mailbox = mailbox;
        _onDraftsChanged = onDraftsChanged;
        _loadAsDraft = loadAsDraft;
        _draftUid = existingDraftUid;
        CcBox.Text = string.Join("; ", settings.AlwaysCc.Where(EmailAddressHelper.IsValid));
        HighPriorityBox.IsChecked = settings.AlwaysSendHighPriority;
        ReadReceiptBox.IsChecked = settings.AlwaysConfirmSend;
        AttachmentList.ItemsSource = _attachments;
        ApplyEditorTheme();
        Theme.Changed += ApplyEditorTheme;
        Closed += (_, _) => Theme.Changed -= ApplyEditorTheme;
        foreach (var font in MailFonts)
            FontBox.Items.Add(font);
        foreach (var size in MailSizes)
            SizeBox.Items.Add(size.ToString("0"));
        FontBox.SelectedItem = "Calibri";
        SizeBox.SelectedItem = "16";

        _signaturePath = SignatureStore.ResolvePath();
        if (_signaturePath is not null)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(Path.GetFullPath(_signaturePath), UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            SignaturePreview.Source = image;
        }

        _draftTimer.Tick += async (_, _) => await SaveDraftAsync(silent: true);

        _suspendDirty = true;
        if (original is not null && loadAsDraft)
            PrefillDraft(original);
        else if (original is not null)
            Prefill(original, forward);
        if (_settings.AlwaysSendHighPriority)
            HighPriorityBox.IsChecked = true;
        if (_settings.AlwaysConfirmSend)
            ReadReceiptBox.IsChecked = true;
        _suspendDirty = false;

        DraftStatusText.Text = loadAsDraft ? "Rascunho aberto" : "Rascunho será salvo automaticamente";
    }

    private void ApplyEditorTheme()
    {
        if (FindResource("Text") is System.Windows.Media.Brush text)
        {
            Editor.Foreground = text;
            Editor.Document.Foreground = text;
        }
        if (FindResource("InputBg") is System.Windows.Media.Brush bg)
            Editor.Background = bg;
        if (FindResource("Blue") is System.Windows.Media.Brush caret)
            Editor.CaretBrush = caret;
    }

    private void PrefillDraft(MimeMessage original)
    {
        ToBox.Text = string.Join("; ", original.To.Mailboxes.Select(m => m.Address));
        var cc = original.Cc.Mailboxes.Select(m => m.Address).ToList();
        if (!cc.Contains(MailConstants.AlwaysCc, StringComparer.OrdinalIgnoreCase))
            cc.Add(MailConstants.AlwaysCc);
        CcBox.Text = string.Join("; ", cc);
        BccBox.Text = string.Join("; ", original.Bcc.Mailboxes.Select(m => m.Address));
        SubjectBox.Text = original.Subject ?? "";
        HighPriorityBox.IsChecked = original.Importance == MessageImportance.High
                                    || original.Priority == MessagePriority.Urgent;
        ReadReceiptBox.IsChecked = original.Headers.Contains(HeaderId.DispositionNotificationTo);

        Editor.Document.Blocks.Clear();
        Editor.Document.Blocks.Add(new Paragraph(new Run(original.TextBody ?? StripTags(original.HtmlBody))) { FontSize = 16 });

        var draftDir = Path.Combine(Path.GetTempPath(), "VRINFO.Mail.Drafts");
        Directory.CreateDirectory(draftDir);
        foreach (var part in original.Attachments.OfType<MimePart>())
        {
            var name = Path.GetFileName(part.FileName ?? "anexo.bin");
            var path = Path.Combine(draftDir, Guid.NewGuid().ToString("N") + "_" + name);
            using var stream = File.Create(path);
            part.Content.DecodeTo(stream);
            _attachments.Add(path);
        }
    }

    private static string StripTags(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "";
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        return System.Net.WebUtility.HtmlDecode(text).Trim();
    }

    private void Prefill(MimeMessage original, bool forward)
    {
        if (!forward)
        {
            ToBox.Text = original.From.Mailboxes.FirstOrDefault()?.Address ?? "";
            SubjectBox.Text = original.Subject?.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) == true
                ? original.Subject
                : "Re: " + original.Subject;
        }
        else
        {
            SubjectBox.Text = original.Subject?.StartsWith("Enc:", StringComparison.OrdinalIgnoreCase) == true
                ? original.Subject
                : "Enc: " + original.Subject;
        }

        Editor.Document.Blocks.Clear();
        Editor.Document.Blocks.Add(new Paragraph(new Run("")));
        Editor.Document.Blocks.Add(new Paragraph(new Run(forward ? "—— encaminhada ——" : "—— mensagem original ——"))
        {
            Foreground = new SolidColorBrush(MediaColor.FromRgb(84, 110, 122)),
            FontSize = 12
        });
        Editor.Document.Blocks.Add(new Paragraph(new Run(original.TextBody ?? "")) { FontSize = 13 });
    }

    private void OnFontChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FontBox.SelectedItem is string font)
            Editor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new WpfFontFamily(font));
        Editor.Focus();
    }

    private void OnSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SizeBox.SelectedItem is string text && double.TryParse(text, out var size))
            Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
        Editor.Focus();
    }

    private void OnBold(object sender, RoutedEventArgs e) => EditingCommands.ToggleBold.Execute(null, Editor);
    private void OnItalic(object sender, RoutedEventArgs e) => EditingCommands.ToggleItalic.Execute(null, Editor);
    private void OnUnderline(object sender, RoutedEventArgs e) => EditingCommands.ToggleUnderline.Execute(null, Editor);
    private void OnAlignLeft(object sender, RoutedEventArgs e) => EditingCommands.AlignLeft.Execute(null, Editor);
    private void OnAlignCenter(object sender, RoutedEventArgs e) => EditingCommands.AlignCenter.Execute(null, Editor);
    private void OnAlignRight(object sender, RoutedEventArgs e) => EditingCommands.AlignRight.Execute(null, Editor);
    private void OnBullets(object sender, RoutedEventArgs e) => EditingCommands.ToggleBullets.Execute(null, Editor);
    private void OnNumbers(object sender, RoutedEventArgs e) => EditingCommands.ToggleNumbering.Execute(null, Editor);

    private void OnClearFormat(object sender, RoutedEventArgs e)
    {
        Editor.Selection.ClearAllProperties();
        Editor.Focus();
    }

    private void OnColor(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.ColorDialog();
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;
        var color = MediaColor.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B);
        Editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(color));
        Editor.Focus();
    }

    private void OnHighlight(object sender, RoutedEventArgs e)
    {
        Editor.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, new SolidColorBrush(MediaColor.FromRgb(255, 249, 196)));
        Editor.Focus();
    }

    private void OnInsertLink(object sender, RoutedEventArgs e)
    {
        var url = Prompt("Endereço do link", "https://");
        if (string.IsNullOrWhiteSpace(url))
            return;
        var label = string.IsNullOrWhiteSpace(Editor.Selection.Text) ? url : Editor.Selection.Text;
        Editor.Selection.Text = string.Empty;
        var link = new Hyperlink(new Run(label));
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            link.NavigateUri = uri;
        Editor.CaretPosition.Paragraph?.Inlines.Add(link);
        Editor.Focus();
    }

    private void OnInsertImage(object sender, RoutedEventArgs e)
    {
        var open = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Imagens|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|Todos|*.*"
        };
        if (open.ShowDialog(this) != true)
            return;

        var options = new ImageInsertWindow { Owner = this };
        if (options.ShowDialog() != true)
            return;

        try
        {
            var optimized = ImageOptimizer.Optimize(open.FileName, options.MaxWidthPx, options.JpegQuality);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(Path.GetFullPath(optimized), UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            var image = new WpfImage
            {
                Source = bitmap,
                Tag = Path.GetFullPath(optimized),
                Stretch = Stretch.Uniform,
                MaxWidth = options.MaxWidthPx,
                SnapsToDevicePixels = true
            };
            Editor.CaretPosition.Paragraph?.Inlines.Add(new InlineUIContainer(image));
            MarkDraftDirty();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, "Não foi possível inserir a imagem: " + ex.Message, MailConstants.ProductName);
        }
        Editor.Focus();
    }

    private void OnAttach(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "Anexar arquivos" };
        if (dialog.ShowDialog(this) != true)
            return;
        foreach (var file in dialog.FileNames)
        {
            if (!_attachments.Contains(file, StringComparer.OrdinalIgnoreCase))
                _attachments.Add(file);
        }
        MarkDraftDirty();
    }

    private void OnRemoveAttachment(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.DataContext is string path)
            _attachments.Remove(path);
        MarkDraftDirty();
    }

    private async void OnSend(object sender, RoutedEventArgs e)
    {
        var to = SplitAddresses(ToBox.Text).ToList();
        if (to.Count == 0)
        {
            System.Windows.MessageBox.Show(this, "Informe ao menos um destinatário.", MailConstants.ProductName);
            return;
        }

        SendButton.IsEnabled = false;
        ComposeRoot.IsEnabled = false;
        SendOverlay.Visibility = Visibility.Visible;
        _draftTimer.Stop();
        _suspendDirty = true;
        SetSendProgress(4, "Preparando envio…");
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);

        try
        {
            SetSendProgress(10, "Formatando o texto da mensagem…");
            var converted = FlowDocumentHtml.Convert(Editor.Document);
            var inlines = converted.Images
                .Select(i => new InlineImagePart(i.Path, i.Cid))
                .ToList();

            var html = converted.Html;
            var text = converted.Text;
            if (_signaturePath is not null)
            {
                SetSendProgress(16, "Aplicando assinatura automática…");
                html += $"""
                    <hr style="border:none;border-top:1px solid #cfd8dc;margin:18px 0 12px 0"/>
                    <div><img src="cid:{MailConstants.SignatureContentId}" alt="VRINFO — Thiago Schwenger" style="max-width:560px;height:auto;border:0"/></div>
                    """;
                text += "\r\n\r\n--\r\nVRINFO — Thiago Schwenger\r\nSuporte técnico";
                inlines.Add(new InlineImagePart(_signaturePath, MailConstants.SignatureContentId));
            }

            var progress = new Progress<(int Percent, string Status)>(p => SetSendProgress(p.Percent, p.Status));
            await SmtpMailSender.SendAsync(
                _settings,
                _password,
                to,
                SplitAddresses(CcBox.Text),
                SplitAddresses(BccBox.Text),
                SubjectBox.Text,
                text,
                html,
                _attachments,
                inlines,
                _forward || _loadAsDraft ? null : _original,
                HighPriorityBox.IsChecked == true,
                ReadReceiptBox.IsChecked == true,
                progress,
                CancellationToken.None);
            SetSendProgress(100, "Enviado com sucesso");
            if (_draftUid is uint uid && uid != 0 && _mailbox is not null)
            {
                try
                {
                    await _mailbox.DeleteAsync(MailConstants.FolderDrafts, uid, CancellationToken.None);
                }
                catch
                {
                    // rascunho já removido
                }
            }

            _sent = true;
            _onDraftsChanged?.Invoke();
            await Task.Delay(280);
            _allowClose = true;
            Close();
        }
        catch (Exception ex)
        {
            SendOverlay.Visibility = Visibility.Collapsed;
            ComposeRoot.IsEnabled = true;
            SendButton.IsEnabled = true;
            _suspendDirty = false;
            System.Windows.MessageBox.Show(this, ex.Message, MailConstants.ProductName);
        }
    }

    private void SetSendProgress(int percent, string status)
    {
        SendProgress.Value = percent;
        SendStatusText.Text = status;
        SendPercentText.Text = percent + "%";
    }

    private string? Prompt(string title, string initial)
    {
        var window = new System.Windows.Window
        {
            Title = title,
            Width = 460,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };
        var box = new System.Windows.Controls.TextBox { Text = initial, Margin = new Thickness(12) };
        var ok = new System.Windows.Controls.Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 12) };
        var cancel = new System.Windows.Controls.Button { Content = "Cancelar", Width = 80, IsCancel = true, Margin = new Thickness(0, 0, 12, 12) };
        string? result = null;
        ok.Click += (_, _) => { result = box.Text; window.DialogResult = true; };
        var buttons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var dock = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        dock.Children.Add(buttons);
        dock.Children.Add(box);
        window.Content = dock;
        return window.ShowDialog() == true ? result : null;
    }

    private static IEnumerable<string> SplitAddresses(string? raw)
        => (raw ?? "").Split([';', ',', '\r', '\n', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private void OnDraftDirty(object sender, RoutedEventArgs e) => MarkDraftDirty();

    private void OnDraftDirty(object sender, System.Windows.Controls.TextChangedEventArgs e) => MarkDraftDirty();

    private void MarkDraftDirty()
    {
        if (_suspendDirty || _sent || _discarded)
            return;
        _draftDirty = true;
        _draftTimer.Stop();
        _draftTimer.Start();
        if (DraftStatusText is not null)
            DraftStatusText.Text = "Alterações serão salvas em Rascunhos…";
    }

    private MimeMessage BuildDraftMime()
    {
        var converted = FlowDocumentHtml.Convert(Editor.Document);
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(
            string.IsNullOrWhiteSpace(_settings.DisplayName) ? _settings.Email : _settings.DisplayName,
            _settings.Email));

        foreach (var address in SplitAddresses(ToBox.Text).Where(EmailAddressHelper.IsValid))
            message.To.Add(MailboxAddress.Parse(address));

        var ccSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var address in SplitAddresses(CcBox.Text).Where(EmailAddressHelper.IsValid))
            ccSet.Add(address);
        if (EmailAddressHelper.IsValid(MailConstants.AlwaysCc))
            ccSet.Add(MailConstants.AlwaysCc);
        ccSet.Remove(_settings.Email);
        foreach (var address in ccSet)
            message.Cc.Add(MailboxAddress.Parse(address));

        foreach (var address in SplitAddresses(BccBox.Text).Where(EmailAddressHelper.IsValid))
            message.Bcc.Add(MailboxAddress.Parse(address));

        message.Subject = SubjectBox.Text ?? "";
        if (HighPriorityBox.IsChecked == true)
        {
            message.Priority = MessagePriority.Urgent;
            message.XPriority = XMessagePriority.High;
            message.Importance = MessageImportance.High;
        }

        if (ReadReceiptBox.IsChecked == true && EmailAddressHelper.IsValid(_settings.Email))
            message.Headers[HeaderId.DispositionNotificationTo] = _settings.Email;

        var builder = new BodyBuilder
        {
            TextBody = converted.Text ?? "",
            HtmlBody = converted.Html
        };
        foreach (var inline in converted.Images)
        {
            if (!File.Exists(inline.Path))
                continue;
            var resource = builder.LinkedResources.Add(Path.GetFullPath(inline.Path));
            resource.ContentId = inline.Cid;
            resource.ContentDisposition = new ContentDisposition(ContentDisposition.Inline);
        }

        foreach (var path in _attachments.Where(File.Exists))
            builder.Attachments.Add(path);

        message.Body = builder.ToMessageBody();
        return message;
    }

    private bool IsDraftEmpty()
    {
        var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
        var body = (range.Text ?? "").Replace("\r", "").Replace("\n", "").Trim();
        return !SplitAddresses(ToBox.Text).Any()
               && string.IsNullOrWhiteSpace(SubjectBox.Text)
               && string.IsNullOrWhiteSpace(body)
               && _attachments.Count == 0;
    }

    private async Task SaveDraftAsync(bool silent)
    {
        _draftTimer.Stop();
        if (_mailbox is null || _sent || _discarded || !_draftDirty)
            return;
        if (IsDraftEmpty())
        {
            DraftStatusText.Text = "Rascunho vazio — ainda não salvo na pasta";
            return;
        }

        await _draftLock.WaitAsync();
        try
        {
            var mime = BuildDraftMime();
            _draftUid = await _mailbox.SaveDraftAsync(mime, _draftUid, CancellationToken.None);
            _draftDirty = false;
            DraftStatusText.Text = "Salvo em Rascunhos · " + DateTime.Now.ToString("HH:mm");
            if (!silent)
                _onDraftsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            DraftStatusText.Text = "Falha ao salvar rascunho";
            if (!silent)
                System.Windows.MessageBox.Show(this, ex.Message, MailConstants.ProductName);
        }
        finally
        {
            _draftLock.Release();
        }
    }

    private async void OnDiscardDraft(object sender, RoutedEventArgs e)
    {
        if (System.Windows.MessageBox.Show(
                this,
                "Excluir este rascunho da pasta Rascunhos?",
                MailConstants.ProductName,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _discarded = true;
        _draftTimer.Stop();
        if (_draftUid is uint uid && uid != 0 && _mailbox is not null)
        {
            try
            {
                await _mailbox.DeleteAsync(MailConstants.FolderDrafts, uid, CancellationToken.None);
            }
            catch
            {
                // já excluído
            }
        }

        _onDraftsChanged?.Invoke();
        _allowClose = true;
        Close();
    }

    private async void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose || _sent || _discarded)
        {
            _draftTimer.Stop();
            return;
        }

        e.Cancel = true;
        if (_closingInProgress)
            return;

        _closingInProgress = true;
        _draftTimer.Stop();
        try
        {
            await SaveDraftAsync(silent: false);
            _allowClose = true;
            await Dispatcher.InvokeAsync(Close, DispatcherPriority.ApplicationIdle);
        }
        finally
        {
            _closingInProgress = false;
        }
    }
}
