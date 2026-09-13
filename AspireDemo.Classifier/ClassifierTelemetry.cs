using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AspireDemo.Classifier;

public static class ClassifierTelemetry
{
    // Matches the application name, which ServiceDefaults already registers as a
    // tracing source, so the classifier's activity and the chat client's span
    // (UseOpenTelemetry is given the same name) are exported without extra wiring.
    public const string SourceName = "AspireDemo.Classifier";

    public static readonly ActivitySource Source = new(SourceName);
    public static readonly Meter Meter = new(SourceName);

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
}
