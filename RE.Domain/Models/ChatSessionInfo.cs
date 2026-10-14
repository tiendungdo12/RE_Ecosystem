using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RE.Domain.Models;

public class ChatSessionInfo : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private string _projectName = string.Empty;
    private string _lastMessageSnippet = string.Empty;
    private bool _isPinned;
    private string _updatedAt = string.Empty;

    public string SessionId { get; set; } = string.Empty;

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string ProjectName
    {
        get => _projectName;
        set => SetProperty(ref _projectName, value);
    }

    public string LastMessageSnippet
    {
        get => _lastMessageSnippet;
        set => SetProperty(ref _lastMessageSnippet, value);
    }

    public bool IsPinned
    {
        get => _isPinned;
        set => SetProperty(ref _isPinned, value);
    }

    public bool IsPlaceholder { get; set; }
    public string CreatedAt { get; set; } = string.Empty;

    public string UpdatedAt
    {
        get => _updatedAt;
        set
        {
            if (SetProperty(ref _updatedAt, value)) OnPropertyChanged(nameof(RelativeTime));
        }
    }

    public string RelativeTime
    {
        get
        {
            if (!DateTime.TryParse(UpdatedAt, out var dt)) return string.Empty;
            var span = DateTime.Now - dt;
            if (span.TotalMinutes < 1) return "just_now";
            if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m";
            if (span.TotalDays < 1) return $"{(int)span.TotalHours}h";
            if (span.TotalDays < 30) return $"{(int)span.TotalDays}d";
            if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30)}mo";
            return $"{(int)(span.TotalDays / 365)}y";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(storage, value)) return false;
        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}