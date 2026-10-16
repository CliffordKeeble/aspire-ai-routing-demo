using AspireDemo.Contracts;

namespace AspireDemo.Web.Services;

// In-memory, bounded store of HandledMessage per category. Registered as a
// singleton so the background relay and the Departments page share state.
// Fires MessageAdded after each Add so subscribers (Blazor components) can
// re-render on the UI thread via InvokeAsync(StateHasChanged).
public sealed class HandledMessageStore
{
    public const int MaxPerCategory = 50;

    private readonly object _gate = new();
    // The Ids currently held, so a redelivered message is ignored. Service Bus
    // delivery is at-least-once. An Id leaves this set when the cap drops it.
    private readonly HashSet<Guid> _held = [];
    private readonly Dictionary<string, LinkedList<HandledMessage>> _byCategory;

    public HandledMessageStore()
    {
        _byCategory = Categories.All.ToDictionary(c => c, _ => new LinkedList<HandledMessage>());
    }

    public event Action? MessageAdded;

    public void Add(HandledMessage message)
    {
        // A message whose category is not in the declared set still shows, under Other.
        var bucket = Categories.All.Contains(message.Category) ? message.Category : Categories.Other;

        lock (_gate)
        {
            if (!_held.Add(message.Id))
            {
                return;
            }

            var list = _byCategory[bucket];
            list.AddLast(message);
            while (list.Count > MaxPerCategory)
            {
                _held.Remove(list.First!.Value.Id);
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
