namespace AspireDemo.Contracts;

public record SupportMessage(Guid Id, string CustomerText, DateTimeOffset ReceivedAt);

public record ClassifiedMessage(Guid Id, string CustomerText, string Category, DateTimeOffset ClassifiedAt);

public record HandledMessage(Guid Id, string Category, string CustomerText, string Response, DateTimeOffset HandledAt);

// The one declared category set. The Classifier reads the list (its prompt and its
// check of a model's reply, so it never publishes a category outside it) and the Web
// reads it (HandledMessageStore's buckets and the Departments page's columns). The
// Handler does not read the list: its response switch names the constants.
public static class Categories
{
    public const string Billing = "Billing";
    public const string Account = "Account";
    public const string Cancellation = "Cancellation";
    public const string Other = "Other";

    public static readonly IReadOnlyList<string> All = [Billing, Account, Cancellation, Other];
}

public static class Topics
{
    public const string Incoming = "support.incoming";
    public const string Classified = "support.classified";
    public const string Handled = "support.handled";
}

public static class Subscriptions
{
    public const string ClassifierSub = "classifier-sub";
    public const string HandlerSub = "handler-sub";
    public const string WebFrontend = "webfrontend-sub";

    // An observer on support.handled for the end-to-end test. The AppHost declares it
    // only when Testing:ObserveHandled is true, so a normal run has no such subscription.
    public const string TestObserver = "test-observer-sub";
}
