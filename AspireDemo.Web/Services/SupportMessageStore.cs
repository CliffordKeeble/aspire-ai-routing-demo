using AspireDemo.Contracts;

namespace AspireDemo.Web.Services;

// In-memory, bounded store of HandledMessage per category. Registered as a
// singleton so the background relay and the Departments page share state.
// Fires MessageAdded after each Add so subscribers (Blazor components) can
// re-render on the UI thread via InvokeAsync(StateHasChanged).
public sealed class SupportMessageStore
{
    public const int MaxPerCategory = 50;

    // Match the handler's canned-response categories. Messages with any other
    // category still route to "Other".
    public static readonly IReadOnlyList<string> Categories =
        ["Billing", "Account", "Cancellation", "Other"];

    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedList<HandledMessage>> _byCategory;

    public SupportMessageStore()
    {
        _byCategory = Categories.ToDictionary(c => c, _ => new LinkedList<HandledMessage>());
    }

    public event Action? MessageAdded;

    public void Add(HandledMessage message)
    {
        var bucket = Categories.Contains(message.Category) ? message.Category : "Other";

        lock (_gate)
        {
            var list = _byCategory[bucket];
            list.AddLast(message);
            while (list.Count > MaxPerCategory)
            {
                list.RemoveFirst();
            }
        }

        MessageAdded?.Invoke();
    }

    public IReadOnlyList<HandledMessage> GetForCategory(string category)
    {
        if (!_byCategory.TryGetValue(category, out _))
        {
            return Array.Empty<HandledMessage>();
        }

        lock (_gate)
        {
            // Snapshot — newest first so the page renders without reversing.
            return _byCategory[category].Reverse().ToArray();
        }
    }
}
