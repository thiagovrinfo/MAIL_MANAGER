using System.Text.Json;
using Vrinfo.Mail.Core;

namespace Vrinfo.Mail.Imap;

public sealed class MailSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string FilePath { get; }

    public MailSettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VRINFO.Mail",
            "settings.json"))
    {
    }

    public MailSettingsStore(string filePath)
    {
        FilePath = filePath;
    }

    public MailSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new MailSettings();

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<MailSettings>(json, JsonOptions) ?? new MailSettings();
        }
        catch
        {
            return new MailSettings();
        }
    }

    public void Save(MailSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    public bool HasConfiguredAccount()
    {
        var settings = Load();
        if (!EmailAddressHelper.IsValid(settings.Email))
            return false;
        return !string.IsNullOrWhiteSpace(WindowsCredentialStore.Read(settings.Email));
    }
}
