using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AspireDemo.Classifier;

public static class ClassifierTelemetry
{
    // Matches the application name, which ServiceDefaults already registers as a
    // tracing source, so the classifier's activity and the chat client's span
    // (UseOpenTelemetry is given the same name) are exported without extra wiring.
    public const string TelemetryName = "AspireDemo.Classifier";

    public const string ClassifyActivityName = "classify message";

    public static readonly ActivitySource Source = new(TelemetryName);
    public static readonly Meter Meter = new(TelemetryName);

    // Every classification, tagged with whether it fell back. The fallback share
    // is the instrument that says whether this is still an AI demo.
    public static readonly Counter<long> Classifications =
        Meter.CreateCounter<long>("classifier.classifications", description: "Messages classified, tagged by provider and fallback.");

    public static class Tags
    {
        public const string Provider = "classifier.provider";
        public const string Fallback = "classifier.fallback";
        public const string FallbackReason = "classifier.fallback_reason";
        public const string Category = "classifier.category";
    }

    public static class FallbackReasons
    {
        public const string NoMatch = "no_match";
        public const string Exception = "exception";
        public const string Timeout = "timeout";
    }

    // The one recording path. Every provider starts its activity here and records
    // its outcome here, so the tag set cannot drift between providers.
    public static Activity? StartClassification(string provider)
    {
        var activity = Source.StartActivity(ClassifyActivityName);
        activity?.SetTag(Tags.Provider, provider);
        return activity;
    }

    public static void RecordClassification(Activity? activity, string provider, string category, string? fallbackReason)
    {
        var fellBack = fallbackReason is not null;

        activity?.SetTag(Tags.Category, category);
        activity?.SetTag(Tags.Fallback, fellBack);
        if (fellBack)
            activity?.SetTag(Tags.FallbackReason, fallbackReason);

        Classifications.Add(
            1,
            new KeyValuePair<string, object?>(Tags.Provider, provider),
            new KeyValuePair<string, object?>(Tags.Fallback, fellBack),
            new KeyValuePair<string, object?>(Tags.FallbackReason, fallbackReason ?? "none"));
    }
}
