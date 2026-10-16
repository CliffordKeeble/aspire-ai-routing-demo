using AspireDemo.Contracts;
using Microsoft.Extensions.AI;

namespace AspireDemo.Classifier;

public sealed record ChatClientClassifierOptions(string Provider, TimeSpan Timeout);

// One model call through IChatClient. The reply must be exactly one category
// word after normalisation; anything else (no match, an exception, a timeout)
// logs a warning and falls back to KeywordClassifier, so the pipeline never
// publishes a category outside Categories.All.
//
// The fallback is decided here, one layer above the chat client, so the chat
// client's own telemetry cannot see it: a junk reply is a successful call from
// its point of view. This class therefore emits its own activity and says on it
// whether the answer came from the model or from the fallback.
public sealed partial class ChatClientClassifier(
    IChatClient chatClient,
    KeywordClassifier fallback,
    ChatClientClassifierOptions options,
    ILogger<ChatClientClassifier> logger) : IMessageClassifier
{
    private const int MaxLoggedReplyLength = 200;

    public static readonly string SystemPrompt =
        "You route customer support messages. Classify the customer's message into exactly one of these " +
        $"categories: {string.Join(", ", Categories.All)}. " +
        "Reply with that single word and nothing else.";

    public async Task<string> ClassifyAsync(string customerText, CancellationToken ct)
    {
        using var activity = ClassifierTelemetry.StartClassification(options.Provider);

        string reason;
        string? reply = null;
        Exception? error = null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.Timeout);

        try
        {
            var response = await chatClient.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, SystemPrompt),
                    new ChatMessage(ChatRole.User, customerText ?? string.Empty),
                ],
                LowestVarianceOptions(),
                timeout.Token);

            reply = response.Text;
            if (TryMatchCategory(reply, out var category))
            {
                ClassifierTelemetry.RecordClassification(activity, options.Provider, category, fallbackReason: null);
                return category;
            }

            reason = ClassifierTelemetry.FallbackReasons.NoMatch;
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            reason = ClassifierTelemetry.FallbackReasons.Timeout;
            error = ex;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            reason = ClassifierTelemetry.FallbackReasons.Exception;
            error = ex;
        }

        var fallbackCategory = await fallback.ClassifyAsync(customerText ?? string.Empty, ct);

        // The warning carries the reply's length, not its text: a model's reply to a
        // customer's message can echo what the customer wrote. The text is at Debug.
        LogFallback(logger, error, options.Provider, reason, reply?.Length ?? 0, fallbackCategory);
        if (reply is not null && logger.IsEnabled(LogLevel.Debug))
            LogRejectedReply(logger, Truncate(reply));

        ClassifierTelemetry.RecordClassification(activity, options.Provider, fallbackCategory, reason);
        return fallbackCategory;
    }

    // Exact match after normalisation, else no match. Normalisation is trim,
    // strip surrounding punctuation, case-fold, and nothing more. No substring
    // search: "I'd route this to Billing rather than Account" names two
    // categories, and a scan would silently pick whichever it met first.
    internal static bool TryMatchCategory(string? reply, out string category)
    {
        var span = (reply ?? string.Empty).AsSpan();

        var start = 0;
        var end = span.Length;
        while (start < end && IsWhitespaceOrPunctuation(span[start])) start++;
        while (end > start && IsWhitespaceOrPunctuation(span[end - 1])) end--;

        var candidate = span[start..end];
        foreach (var known in Categories.All)
        {
            if (candidate.Equals(known, StringComparison.OrdinalIgnoreCase))
            {
                category = known;
                return true;
            }
        }

        category = string.Empty;
        return false;
    }

    private static bool IsWhitespaceOrPunctuation(char c) => char.IsWhiteSpace(c) || char.IsPunctuation(c);

    // Lowest-variance settings: greedy decoding and a reply budget sized for one
    // word. Greedy decoding still varies with batching, floating point and model
    // updates, which is why the integration suite does not run a model.
    private static ChatOptions LowestVarianceOptions() => new()
    {
        Temperature = 0,
        MaxOutputTokens = 16,
    };

    private static string Truncate(string reply) =>
        reply.Length > MaxLoggedReplyLength ? reply[..MaxLoggedReplyLength] + "…" : reply;

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Classifier fell back to keyword matching. Provider={Provider} Reason={FallbackReason} ReplyLength={ReplyLength} Category={Category}")]
    private static partial void LogFallback(
        ILogger logger, Exception? exception, string provider, string fallbackReason, int replyLength, string category);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Model reply that was not a category: {Reply}")]
    private static partial void LogRejectedReply(ILogger logger, string reply);
}
