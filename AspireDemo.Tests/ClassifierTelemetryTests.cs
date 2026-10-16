using System.Diagnostics;
using System.Diagnostics.Metrics;
using AspireDemo.Classifier;
using AspireDemo.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AspireDemo.Tests;

// Every provider records one `classify message` activity and one count, with the
// same tag set. The names are literals on purpose: a rename of either is a change
// to what a dashboard shows, and it should fail here.
public class ClassifierTelemetryTests
{
    private const string ActivityName = "classify message";
    private const string CounterName = "classifier.classifications";

    [Fact]
    public async Task Keyword_provider_records_one_activity_and_one_count()
    {
        using var probe = new TelemetryProbe();
        using var host = BuildHost(("Classifier:Provider", "keyword"));
        var classifier = host.Services.GetRequiredService<IMessageClassifier>();

        var category = await classifier.ClassifyAsync("my invoice is wrong", TestContext.Current.CancellationToken);

        Assert.Equal(Categories.Billing, category);
        var activity = Assert.Single(probe.Activities(ActivityName));
        Assert.Equal("keyword", activity.GetTagItem(ClassifierTelemetry.Tags.Provider));
        Assert.Equal(false, activity.GetTagItem(ClassifierTelemetry.Tags.Fallback));
        Assert.Equal(Categories.Billing, activity.GetTagItem(ClassifierTelemetry.Tags.Category));
        Assert.Null(activity.GetTagItem(ClassifierTelemetry.Tags.FallbackReason));

        var count = Assert.Single(probe.Counts(CounterName));
        Assert.Equal(1, count.Value);
        Assert.Equal("keyword", count.Tags[ClassifierTelemetry.Tags.Provider]);
        Assert.Equal(false, count.Tags[ClassifierTelemetry.Tags.Fallback]);
        Assert.Equal("none", count.Tags[ClassifierTelemetry.Tags.FallbackReason]);
    }

    [Fact]
    public async Task Chat_client_provider_records_one_activity_and_one_count()
    {
        using var probe = new TelemetryProbe();
        var classifier = new ChatClientClassifier(
            new FakeChatClient((_, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Account")))),
            new KeywordClassifier(),
            new ChatClientClassifierOptions("fake", TimeSpan.FromSeconds(5)),
            new ListLogger<ChatClientClassifier>());

        var category = await classifier.ClassifyAsync("hello there", TestContext.Current.CancellationToken);

        Assert.Equal(Categories.Account, category);
        var activity = Assert.Single(probe.Activities(ActivityName));
        Assert.Equal("fake", activity.GetTagItem(ClassifierTelemetry.Tags.Provider));
        Assert.Equal(false, activity.GetTagItem(ClassifierTelemetry.Tags.Fallback));
        Assert.Equal(Categories.Account, activity.GetTagItem(ClassifierTelemetry.Tags.Category));

        var count = Assert.Single(probe.Counts(CounterName));
        Assert.Equal(1, count.Value);
        Assert.Equal("fake", count.Tags[ClassifierTelemetry.Tags.Provider]);
        Assert.Equal(false, count.Tags[ClassifierTelemetry.Tags.Fallback]);
        Assert.Equal("none", count.Tags[ClassifierTelemetry.Tags.FallbackReason]);
    }

    [Fact]
    public async Task Chat_client_fallback_is_counted_once_not_twice()
    {
        using var probe = new TelemetryProbe();
        var classifier = new ChatClientClassifier(
            new FakeChatClient((_, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "banana")))),
            new KeywordClassifier(),
            new ChatClientClassifierOptions("fake", TimeSpan.FromSeconds(5)),
            new ListLogger<ChatClientClassifier>());

        await classifier.ClassifyAsync("my invoice is wrong", TestContext.Current.CancellationToken);

        Assert.Single(probe.Activities(ActivityName));
        var count = Assert.Single(probe.Counts(CounterName));
        Assert.Equal(true, count.Tags[ClassifierTelemetry.Tags.Fallback]);
        Assert.Equal("no_match", count.Tags[ClassifierTelemetry.Tags.FallbackReason]);
    }

    private static IHost BuildHost(params (string Key, string Value)[] settings) => ClassifierTestHost.Build(settings);
}

// Listens to the classifier's own source and meter for the life of one test.
// Tests in this assembly do not run in parallel, so nothing else is classifying.
internal sealed class TelemetryProbe : IDisposable
{
    private readonly ActivityListener _activityListener;
    private readonly MeterListener _meterListener = new();
    private readonly List<Activity> _activities = [];
    private readonly List<(string Instrument, long Value, Dictionary<string, object?> Tags)> _counts = [];

    public TelemetryProbe()
    {
        _activityListener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == ClassifierTelemetry.TelemetryName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => { lock (_activities) _activities.Add(a); },
        };
        ActivitySource.AddActivityListener(_activityListener);

        _meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == ClassifierTelemetry.TelemetryName)
                listener.EnableMeasurementEvents(instrument);
        };
        _meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var copy = new Dictionary<string, object?>();
            foreach (var tag in tags) copy[tag.Key] = tag.Value;
            lock (_counts) _counts.Add((instrument.Name, value, copy));
        });
        _meterListener.Start();
    }

    public IReadOnlyList<Activity> Activities(string operationName)
    {
        lock (_activities) return _activities.Where(a => a.OperationName == operationName).ToList();
    }

    public IReadOnlyList<(long Value, Dictionary<string, object?> Tags)> Counts(string instrumentName)
    {
        lock (_counts) return _counts.Where(c => c.Instrument == instrumentName).Select(c => (c.Value, c.Tags)).ToList();
    }

    public void Dispose()
    {
        _activityListener.Dispose();
        _meterListener.Dispose();
    }
}

internal sealed class FakeChatClient(
    Func<IEnumerable<ChatMessage>, CancellationToken, Task<ChatResponse>> respond) : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        respond(messages, cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
