using System.Collections.ObjectModel;
using RE.Domain.Models;

namespace RE.Presentation.ViewModels;

public sealed class SessionGroup : ObservableCollection<ChatSessionInfo>
{
    public string Key { get; }
    public bool IsNested { get; }
    public bool IsHeaderOnly { get; }

    public SessionGroup(string key, IEnumerable<ChatSessionInfo> items, bool isNested = false,
        bool isHeaderOnly = false)
        : base(items)
    {
        Key = key;
        IsNested = isNested;
        IsHeaderOnly = isHeaderOnly;
    }
}