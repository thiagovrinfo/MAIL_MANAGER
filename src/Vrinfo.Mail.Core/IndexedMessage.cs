using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Vrinfo.Mail.Core;

public sealed class IndexedMessage : INotifyPropertyChanged
{
    private bool _isSeen;

    public string UniqueId { get; set; } = string.Empty;
    public string Folder { get; set; } = string.Empty;
    public uint Uid { get; set; }
    public string MessageId { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
    public string ToAddresses { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Preview { get; set; } = string.Empty;
    public DateTime DateUtc { get; set; }

    public bool IsSeen
    {
        get => _isSeen;
        set
        {
            if (_isSeen == value)
                return;
            _isSeen = value;
            OnPropertyChanged();
        }
    }

    public bool IsFlagged { get; set; }
    public bool HasAttachment { get; set; }
    public bool IsFiscal { get; set; }
    public bool HasUnsubscribe { get; set; }
    public bool IsContabilidade { get; set; }
    public MessagePriorityLevel Priority { get; set; } = MessagePriorityLevel.Normal;

    public string DisplayFrom => string.IsNullOrWhiteSpace(FromName) ? FromAddress : FromName;
    public string WhenLabel => DateUtc.ToLocalTime().ToString("dd/MM HH:mm");
    public string WhenShort => DateUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    public bool ShowPreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Preview))
                return false;
            return !string.Equals(Preview.Trim(), Subject.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
    public bool IsHighPriority => Priority == MessagePriorityLevel.High;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
