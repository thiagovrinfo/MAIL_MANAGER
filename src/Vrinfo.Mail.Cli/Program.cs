using Vrinfo.Mail.Core;
using Vrinfo.Mail.Imap;

var store = new MailSettingsStore();
var settings = store.Load();
var password = WindowsCredentialStore.Read(settings.Email);
if (string.IsNullOrWhiteSpace(settings.Email) || string.IsNullOrWhiteSpace(password))
{
    Console.WriteLine("Conta não configurada neste computador.");
    return 1;
}

using var mailbox = new ImapMailbox();
using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
Console.WriteLine("Conectando " + settings.Email + "…");
await mailbox.ConnectAsync(settings, password, cts.Token);

var moved = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
Console.WriteLine("Separando histórico (Inovafarma, Hiper, Discord, Contabilidade)…");
await mailbox.ApplyMailboxSmartRulesAsync(msg =>
{
    moved[msg.Folder] = moved.GetValueOrDefault(msg.Folder) + 1;
    Console.WriteLine("  → " + msg.Folder + " | " + msg.FromAddress + " | " + msg.Subject);
}, cts.Token);

Console.WriteLine();
Console.WriteLine("Movimentações nesta execução:");
if (moved.Count == 0)
    Console.WriteLine("  (nenhuma — pastas já estavam separadas ou não houve match)");
foreach (var pair in moved.OrderByDescending(p => p.Value))
    Console.WriteLine($"  {pair.Key}: {pair.Value}");

Console.WriteLine();
Console.WriteLine("Marcando toda a caixa como lida…");
await mailbox.MarkMailboxReadAsync(cts.Token);

Console.WriteLine();
Console.WriteLine("Remetentes que ainda restam na Entrada (candidatos a pasta própria):");
var senders = await mailbox.CountInboxSendersAsync(cts.Token);
const int minRepeat = 5;
var frequent = senders.Where(s => s.Count >= minRepeat).ToList();
if (frequent.Count == 0)
{
    Console.WriteLine("  Nenhum remetente com 5 ou mais mensagens na Entrada.");
    Console.WriteLine("  Top 15 mesmo assim:");
    foreach (var s in senders.Take(15))
        Console.WriteLine($"  {s.Count,5}  {s.Address}  ({s.DisplayName})");
}
else
{
    foreach (var s in frequent)
        Console.WriteLine($"  {s.Count,5}  {s.Address}  ({s.DisplayName})");
}

var report = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "VRINFO.Mail",
    "remetentes-entrada.txt");
Directory.CreateDirectory(Path.GetDirectoryName(report)!);
File.WriteAllLines(report, senders.Select(s => $"{s.Count}\t{s.Address}\t{s.DisplayName}"));
Console.WriteLine();
Console.WriteLine("Relatório completo: " + report);
return 0;
