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

        store.Add(Make(Categories.Billing));

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Add_ignores_an_Id_it_already_holds()
    {
        // Service Bus delivery is at-least-once, so the relay can see one message twice.
        var store = new SupportMessageStore();
        var raised = 0;
        store.MessageAdded += () => raised++;
        var message = Make(Categories.Billing);

        store.Add(message);
        store.Add(message);

        Assert.Equal(message.Id, Assert.Single(store.GetForCategory(Categories.Billing)).Id);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Add_accepts_an_Id_again_once_the_cap_has_dropped_it()
    {
        var store = new SupportMessageStore();
        var first = Make(Categories.Billing, "first");
        store.Add(first);
        for (var i = 0; i < SupportMessageStore.MaxPerCategory; i++)
        {
            store.Add(Make(Categories.Billing, $"msg {i}"));
        }
        Assert.DoesNotContain(store.GetForCategory(Categories.Billing), m => m.Id == first.Id);

        store.Add(first);

        Assert.Contains(store.GetForCategory(Categories.Billing), m => m.Id == first.Id);
    }

    [Fact]
    public void Add_routes_to_correct_category_only()
    {
        var store = new SupportMessageStore();
        var billing = Make(Categories.Billing);
        store.Add(billing);

        Assert.Equal(billing.Id, Assert.Single(store.GetForCategory(Categories.Billing)).Id);
        Assert.Empty(store.GetForCategory(Categories.Account));
        Assert.Empty(store.GetForCategory(Categories.Cancellation));
        Assert.Empty(store.GetForCategory(Categories.Other));
    }

    [Fact]
    public void Add_with_unknown_category_falls_into_Other()
    {
        var store = new SupportMessageStore();
        var mystery = Make("Gibberish");
        store.Add(mystery);

        Assert.Equal(mystery.Id, Assert.Single(store.GetForCategory(Categories.Other)).Id);
        Assert.Empty(store.GetForCategory(Categories.Billing));
    }

    [Fact]
    public void Add_enforces_per_category_cap_dropping_oldest()
    {
        var store = new SupportMessageStore();
        var messages = Enumerable.Range(0, SupportMessageStore.MaxPerCategory + 1)
            .Select(i => Make(Categories.Billing, $"msg {i}"))
            .ToArray();

        foreach (var m in messages)
        {
            store.Add(m);
        }

        var snapshot = store.GetForCategory(Categories.Billing);
        Assert.Equal(SupportMessageStore.MaxPerCategory, snapshot.Count);
        // Newest first, so the dropped one is the very first message we added.
        Assert.DoesNotContain(snapshot, m => m.Id == messages[0].Id);
        Assert.Contains(snapshot, m => m.Id == messages[^1].Id);
    }

    [Fact]
    public void GetForCategory_returns_snapshot_not_live_view()
    {
        var store = new SupportMessageStore();
        store.Add(Make(Categories.Billing));

        var first = store.GetForCategory(Categories.Billing);
        store.Add(Make(Categories.Billing));
        var second = store.GetForCategory(Categories.Billing);

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
