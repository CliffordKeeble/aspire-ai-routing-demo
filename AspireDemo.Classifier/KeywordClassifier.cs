namespace AspireDemo.Classifier;

// Stub keyword-based implementation. Replaced by an Azure OpenAI implementation
// behind the same IMessageClassifier seam in a later slice.
public sealed class KeywordClassifier : IMessageClassifier
{
    public Task<string> ClassifyAsync(string customerText, CancellationToken ct)
    {
        var text = customerText ?? string.Empty;

        string category =
            ContainsAny(text, "invoice", "bill", "charge") ? "Billing"
            : ContainsAny(text, "password", "login", "log in", "locked out") ? "Account"
            : ContainsAny(text, "cancel", "unsubscribe") ? "Cancellation"
            : "Other";

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
