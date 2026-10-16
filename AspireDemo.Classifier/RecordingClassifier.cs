namespace AspireDemo.Classifier;

// Records the telemetry for a classifier that records none itself. Used for the
// keyword provider. KeywordClassifier stays silent because it is also
// ChatClientClassifier's fallback, and recording there would count a fallback twice.
public sealed class RecordingClassifier(IMessageClassifier inner, string provider) : IMessageClassifier
{
    public async Task<string> ClassifyAsync(string customerText, CancellationToken ct)
    {
        using var activity = ClassifierTelemetry.StartClassification(provider);

        var category = await inner.ClassifyAsync(customerText, ct);

        ClassifierTelemetry.RecordClassification(activity, provider, category, fallbackReason: null);
        return category;
    }
}
