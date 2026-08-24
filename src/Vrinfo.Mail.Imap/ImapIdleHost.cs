using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using Vrinfo.Mail.Core;

namespace Vrinfo.Mail.Imap;

public sealed class ImapIdleHost : IDisposable
{
    public event EventHandler? InboxChanged;

    private CancellationTokenSource? _loopCts;
    private Task? _loop;
    private DateTime _lastRaiseUtc = DateTime.MinValue;

    public void Start(MailSettings settings, string password)
    {
        Stop();
        _loopCts = new CancellationTokenSource();
        var token = _loopCts.Token;
        _loop = Task.Run(() => RunAsync(settings, password, token), token);
    }

    public void Stop()
    {
        try
        {
            _loopCts?.Cancel();
            _loopCts?.Dispose();
        }
        catch
        {
            // ignore
        }

        _loopCts = null;
        _loop = null;
    }

    private async Task RunAsync(MailSettings settings, string password, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var client = new ImapClient { Timeout = 90_000 };
                await client.ConnectAsync(settings.ImapHost, settings.ImapPort, SecureSocketOptions.SslOnConnect, cancellationToken);
                await client.AuthenticateAsync(settings.Email, password, cancellationToken);
                await client.Inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
                client.Inbox.CountChanged += OnInboxChanged;
                client.Inbox.MessageExpunged += OnInboxChanged;

                while (!cancellationToken.IsCancellationRequested)
                {
                    using var done = new CancellationTokenSource();
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, done.Token);
                    done.CancelAfter(TimeSpan.FromMinutes(8));
                    try
                    {
                        await client.IdleAsync(linked.Token);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        // refresh IDLE
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private void OnInboxChanged(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastRaiseUtc).TotalMilliseconds < 2500)
            return;
        _lastRaiseUtc = now;
        InboxChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => Stop();
}
