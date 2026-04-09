using AspireDemo.Contracts;
using AspireDemo.Web.Services;

namespace AspireDemo.Tests;

public class SupportMessageStoreTests
{
    [Fact]
    public void Add_raises_MessageAdded_event()
    {
        var store = new SupportMessageStore();
        var raised = 0;
        store.MessageAdded += () => raised++;

        store.Add(Make("Billing"));

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Add_routes_to_correct_category_only()
    {
        var store = new SupportMessageStore();
        var billing = Make("Billing");
        store.Add(billing);

        Assert.Equal(billing.Id, Assert.Single(store.GetForCategory("Billing")).Id);
        Assert.Empty(store.GetForCategory("Account"));
        Assert.Empty(store.GetForCategory("Cancellation"));
        Assert.Empty(store.GetForCategory("Other"));
    }

    [Fact]
    public void Add_with_unknown_category_falls_into_Other()
    {
        var store = new SupportMessageStore();
        var mystery = Make("Gibberish");
        store.Add(mystery);

        Assert.Equal(mystery.Id, Assert.Single(store.GetForCategory("Other")).Id);
        Assert.Empty(store.GetForCategory("Billing"));
    }

    [Fact]
    public void Add_enforces_per_category_cap_dropping_oldest()
    {
        var store = new SupportMessageStore();
        var messages = Enumerable.Range(0, SupportMessageStore.MaxPerCategory + 1)
            .Select(i => Make("Billing", $"msg {i}"))
            .ToArray();

        foreach (var m in messages)
        {
            store.Add(m);
        }

        var snapshot = store.GetForCategory("Billing");
        Assert.Equal(SupportMessageStore.MaxPerCategory, snapshot.Count);
        // Newest first, so the dropped one is the very first message we added.
        Assert.DoesNotContain(snapshot, m => m.Id == messages[0].Id);
        Assert.Contains(snapshot, m => m.Id == messages[^1].Id);
    }

    [Fact]
    public void GetForCategory_returns_snapshot_not_live_view()
    {
        var store = new SupportMessageStore();
        store.Add(Make("Billing"));

        var first = store.GetForCategory("Billing");
        store.Add(Make("Billing"));
        var second = store.GetForCategory("Billing");

        Assert.Single(first);
        Assert.Equal(2, second.Count);
    }

    [Fact]
    public void GetForCategory_unknown_category_returns_empty()
    {
        var store = new SupportMessageStore();
        Assert.Empty(store.GetForCategory("DoesNotExist"));
    }

    private static HandledMessage Make(string category, string? text = null) =>
        new(
            Guid.NewGuid(),
            category,
            text ?? "original text",
            "canned response",
            DateTimeOffset.UtcNow);
}
