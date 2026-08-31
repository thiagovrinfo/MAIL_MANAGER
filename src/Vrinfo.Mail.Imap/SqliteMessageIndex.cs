using Microsoft.Data.Sqlite;
using Vrinfo.Mail.Core;

namespace Vrinfo.Mail.Imap;

public sealed class SqliteMessageIndex : IDisposable
{
    private readonly object _sync = new();
    private readonly SqliteConnection _connection;

    public SqliteMessageIndex()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VRINFO.Mail",
            "index.db"))
    {
    }

    public SqliteMessageIndex(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS messages (
              unique_id TEXT PRIMARY KEY,
              folder TEXT NOT NULL,
              uid INTEGER NOT NULL,
              message_id TEXT,
              from_address TEXT,
              from_name TEXT,
              to_addresses TEXT,
              subject TEXT,
              preview TEXT,
              date_utc TEXT,
              is_seen INTEGER,
              is_flagged INTEGER,
              has_attachment INTEGER,
              is_fiscal INTEGER,
              has_unsubscribe INTEGER,
              is_contabilidade INTEGER,
              priority INTEGER
            );
            CREATE INDEX IF NOT EXISTS idx_messages_folder ON messages(folder);
            CREATE INDEX IF NOT EXISTS idx_messages_folder_date ON messages(folder, date_utc DESC);
            """;
        cmd.ExecuteNonQuery();
        using var pragma = _connection.CreateCommand();
        pragma.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            PRAGMA cache_size=-2048;
            PRAGMA mmap_size=0;
            PRAGMA temp_store=FILE;
            """;
        pragma.ExecuteNonQuery();
    }

    public void Upsert(IndexedMessage message)
    {
        lock (_sync)
            UpsertCore(message, null);
    }

    private void UpsertCore(IndexedMessage message, SqliteTransaction? transaction)
    {
        using var cmd = _connection.CreateCommand();
        if (transaction is not null)
            cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO messages (
              unique_id, folder, uid, message_id, from_address, from_name, to_addresses,
              subject, preview, date_utc, is_seen, is_flagged, has_attachment, is_fiscal,
              has_unsubscribe, is_contabilidade, priority)
            VALUES (
              $id, $folder, $uid, $mid, $from, $fromName, $to,
              $subject, $preview, $date, $seen, $flagged, $att, $fiscal,
              $unsub, $contab, $pri)
            ON CONFLICT(unique_id) DO UPDATE SET
              folder=excluded.folder, uid=excluded.uid, from_address=excluded.from_address,
              from_name=excluded.from_name, to_addresses=excluded.to_addresses,
              subject=excluded.subject, preview=excluded.preview, date_utc=excluded.date_utc,
              is_seen=excluded.is_seen, is_flagged=excluded.is_flagged,
              has_attachment=excluded.has_attachment, is_fiscal=excluded.is_fiscal,
              has_unsubscribe=excluded.has_unsubscribe, is_contabilidade=excluded.is_contabilidade,
              priority=excluded.priority;
            """;
        cmd.Parameters.AddWithValue("$id", message.UniqueId);
        cmd.Parameters.AddWithValue("$folder", message.Folder);
        cmd.Parameters.AddWithValue("$uid", (long)message.Uid);
        cmd.Parameters.AddWithValue("$mid", message.MessageId);
        cmd.Parameters.AddWithValue("$from", message.FromAddress);
        cmd.Parameters.AddWithValue("$fromName", message.FromName);
        cmd.Parameters.AddWithValue("$to", message.ToAddresses);
        cmd.Parameters.AddWithValue("$subject", message.Subject);
        cmd.Parameters.AddWithValue("$preview", message.Preview);
        cmd.Parameters.AddWithValue("$date", message.DateUtc.ToString("o"));
        cmd.Parameters.AddWithValue("$seen", message.IsSeen ? 1 : 0);
        cmd.Parameters.AddWithValue("$flagged", message.IsFlagged ? 1 : 0);
        cmd.Parameters.AddWithValue("$att", message.HasAttachment ? 1 : 0);
        cmd.Parameters.AddWithValue("$fiscal", message.IsFiscal ? 1 : 0);
        cmd.Parameters.AddWithValue("$unsub", message.HasUnsubscribe ? 1 : 0);
        cmd.Parameters.AddWithValue("$contab", message.IsContabilidade ? 1 : 0);
        cmd.Parameters.AddWithValue("$pri", (int)message.Priority);
        cmd.ExecuteNonQuery();
    }

    public void ReplaceFolder(string folder, IEnumerable<IndexedMessage> messages)
    {
        lock (_sync)
        {
            using var tx = _connection.BeginTransaction();
            using (var del = _connection.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM messages WHERE folder=$f";
                del.Parameters.AddWithValue("$f", folder);
                del.ExecuteNonQuery();
            }

            foreach (var message in messages)
                UpsertCore(message, tx);

            tx.Commit();
        }
    }

    public void DeleteByUniqueId(string uniqueId)
    {
        lock (_sync)
        {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM messages WHERE unique_id=$id";
        cmd.Parameters.AddWithValue("$id", uniqueId);
        cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<IndexedMessage> Query(
        string? folder,
        string? search,
        bool unreadOnly,
        bool attachmentsOnly,
        bool todayOnly,
        bool highPriorityOnly,
        bool fiscalOnly)
    {
        lock (_sync)
        {
        using var cmd = _connection.CreateCommand();
        var sql = "SELECT * FROM messages WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(folder))
        {
            sql += " AND folder=$folder";
            cmd.Parameters.AddWithValue("$folder", folder);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (subject LIKE $q OR from_address LIKE $q OR from_name LIKE $q OR preview LIKE $q)";
            cmd.Parameters.AddWithValue("$q", "%" + search.Trim() + "%");
        }

        if (unreadOnly)
            sql += " AND is_seen=0";
        if (attachmentsOnly)
            sql += " AND has_attachment=1";
        if (highPriorityOnly)
            sql += " AND priority>=2";
        if (fiscalOnly)
            sql += " AND is_fiscal=1";
        if (todayOnly)
        {
            sql += " AND date_utc>=$today";
            cmd.Parameters.AddWithValue("$today", DateTime.UtcNow.Date.ToString("o"));
        }

        sql += " ORDER BY date_utc DESC LIMIT 500";
        cmd.CommandText = sql;
        return ReadAll(cmd);
        }
    }

    public int UnreadCount(string folder)
    {
        lock (_sync)
        {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM messages WHERE folder=$f AND is_seen=0";
        cmd.Parameters.AddWithValue("$f", folder);
        return Convert.ToInt32(cmd.ExecuteScalar());
        }
    }

    private static List<IndexedMessage> ReadAll(SqliteCommand cmd)
    {
        var list = new List<IndexedMessage>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new IndexedMessage
            {
                UniqueId = reader["unique_id"]?.ToString() ?? "",
                Folder = reader["folder"]?.ToString() ?? "",
                Uid = Convert.ToUInt32(reader["uid"]),
                MessageId = reader["message_id"]?.ToString() ?? "",
                FromAddress = reader["from_address"]?.ToString() ?? "",
                FromName = reader["from_name"]?.ToString() ?? "",
                ToAddresses = reader["to_addresses"]?.ToString() ?? "",
                Subject = reader["subject"]?.ToString() ?? "",
                Preview = reader["preview"]?.ToString() ?? "",
                DateUtc = DateTime.TryParse(reader["date_utc"]?.ToString(), out var dt) ? dt : DateTime.MinValue,
                IsSeen = Convert.ToInt32(reader["is_seen"]) == 1,
                IsFlagged = Convert.ToInt32(reader["is_flagged"]) == 1,
                HasAttachment = Convert.ToInt32(reader["has_attachment"]) == 1,
                IsFiscal = Convert.ToInt32(reader["is_fiscal"]) == 1,
                HasUnsubscribe = Convert.ToInt32(reader["has_unsubscribe"]) == 1,
                IsContabilidade = Convert.ToInt32(reader["is_contabilidade"]) == 1,
                Priority = (MessagePriorityLevel)Convert.ToInt32(reader["priority"])
            });
        }

        return list;
    }

    public void Dispose() => _connection.Dispose();
}
