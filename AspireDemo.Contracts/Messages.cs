namespace AspireDemo.Contracts;

public record SupportMessage(Guid Id, string CustomerText, DateTimeOffset ReceivedAt);

public record ClassifiedMessage(Guid Id, string CustomerText, string Category, DateTimeOffset ClassifiedAt);

public record HandledMessage(Guid Id, string Category, string OriginalText, string Response, DateTimeOffset HandledAt);

// The one declared category set. A ClassifiedMessage.Category is always one of
// these: the classifier prompt, the reply validator and the Handler's routing
// all read this list, so nothing downstream can see a value outside it.
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
}
