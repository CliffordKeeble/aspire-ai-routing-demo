using AspireDemo.Contracts;

namespace AspireDemo.Classifier;

// Keyword matching: same input, same answer, no network. The default provider,
// the fallback behind ChatClientClassifier, and what the integration suite runs.
public sealed class KeywordClassifier : IMessageClassifier
{
    public Task<string> ClassifyAsync(string customerText, CancellationToken ct)
    {
        var text = customerText ?? string.Empty;

        string category =
            ContainsAny(text, "invoice", "bill", "charge") ? Categories.Billing
            : ContainsAny(text, "password", "login", "log in", "locked out") ? Categories.Account
            : ContainsAny(text, "cancel", "unsubscribe") ? Categories.Cancellation
            : Categories.Other;

        return Task.FromResult(category);
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
