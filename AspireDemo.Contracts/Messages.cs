namespace AspireDemo.Contracts;

public record SupportMessage(Guid Id, string CustomerText, DateTimeOffset ReceivedAt);

public record ClassifiedMessage(Guid Id, string CustomerText, string Category, DateTimeOffset ClassifiedAt);

public record HandledMessage(Guid Id, string Category, string OriginalText, string Response, DateTimeOffset HandledAt);

public interface IMessageClassifier
{
    Task<string> ClassifyAsync(string customerText, CancellationToken ct);
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
