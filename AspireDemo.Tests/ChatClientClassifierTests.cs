using System.Diagnostics;
using AspireDemo.Classifier;
using AspireDemo.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AspireDemo.Tests;

// ChatClientClassifier against a fake IChatClient: no network, no container.
// "hello there" classifies as Other by keyword, so a fallback is visible as Other
// even when the model's reply names another category.
public class ChatClientClassifierTests
{
    private const string NoKeywordText = "hello there";

    [Theory]
    [InlineData("Billing", Categories.Billing)]
    [InlineData("Billing.", Categories.Billing)]
    [InlineData(" billing ", Categories.Billing)]
    [InlineData("ACCOUNT", Categories.Account)]
    [InlineData("\"Cancellation\"", Categories.Cancellation)]
    [InlineData("Other\n", Categories.Other)]
    public async Task Model_reply_that_normalises_to_a_category_maps_through(string reply, string expected)
    {
        var (classifier, logger) = Create(Reply(reply));

        var category = await classifier.ClassifyAsync(NoKeywordText, TestContext.Current.CancellationToken);

        Assert.Equal(expected, category);
        Assert.Empty(logger.Warnings);
    }

    [Theory]
    [InlineData("Category: Billing")]
    [InlineData("I'd route this to Billing rather than Account")]
    [InlineData("Billing Account")]
    [InlineData("banana")]
    [InlineData("")]
    public async Task Model_reply_that_is_not_exactly_a_category_falls_back_and_logs(string reply)
    {
        var (classifier, logger) = Create(Reply(reply));

        var category = await classifier.ClassifyAsync(NoKeywordText, TestContext.Current.CancellationToken);

        Assert.Equal(Categories.Other, category);
        var warning = Assert.Single(logger.Warnings);
        Assert.Contains("Reason=no_match", warning.Message);
    }

    [Fact]
    public async Task Fallback_warning_carries_the_reply_length_and_the_reply_text_goes_to_debug()
    {
        const string reply = "Category: Billing";
        var (classifier, logger) = Create(Reply(reply));

        await classifier.ClassifyAsync(NoKeywordText, TestContext.Current.CancellationToken);

        var warning = Assert.Single(logger.Warnings);
        Assert.Contains($"ReplyLength={reply.Length}", warning.Message);
        Assert.DoesNotContain(reply, warning.Message);
        var debug = Assert.Single(logger.At(LogLevel.Debug));
        Assert.Contains(reply, debug.Message);
    }

    [Fact]
    public async Task Throwing_client_falls_back_and_logs()
    {
        var (classifier, logger) = Create((_, _) => throw new HttpRequestException("connection refused"));

        var category = await classifier.ClassifyAsync("my invoice is wrong", TestContext.Current.CancellationToken);

        Assert.Equal(Categories.Billing, category);
        var warning = Assert.Single(logger.Warnings);
        Assert.Contains("Reason=exception", warning.Message);
        Assert.IsType<HttpRequestException>(warning.Exception);
    }

    [Fact]
    public async Task Slow_client_times_out_falls_back_and_logs()
    {
        var (classifier, logger) = Create(
            async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new ChatResponse(); },
            timeout: TimeSpan.FromMilliseconds(50));

        var category = await classifier.ClassifyAsync("I want to cancel", TestContext.Current.CancellationToken);

        Assert.Equal(Categories.Cancellation, category);
        var warning = Assert.Single(logger.Warnings);
        Assert.Contains("Reason=timeout", warning.Message);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_rather_than_falling_back()
    {
        var (classifier, logger) = Create(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new ChatResponse(); });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => classifier.ClassifyAsync(NoKeywordText, cts.Token));
        Assert.Empty(logger.Warnings);
    }

    [Theory]
    [InlineData("Billing", false, null)]
    [InlineData("Category: Billing", true, "no_match")]
    public async Task Activity_says_whether_the_answer_came_from_the_fallback(string reply, bool expectFallback, string? expectReason)
    {
        using var testSource = new ActivitySource("AspireDemo.Tests." + nameof(Activity_says_whether_the_answer_came_from_the_fallback));
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == testSource.Name || s.Name == ClassifierTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => { lock (stopped) stopped.Add(a); },
        };
        ActivitySource.AddActivityListener(listener);

        var (classifier, _) = Create(Reply(reply));
        ActivityTraceId traceId;
        using (var parent = testSource.StartActivity("test")!)
        {
            traceId = parent.TraceId;
            await classifier.ClassifyAsync(NoKeywordText, TestContext.Current.CancellationToken);
        }

        Activity classify;
        lock (stopped)
            classify = Assert.Single(stopped, a => a.Source.Name == ClassifierTelemetry.SourceName && a.TraceId == traceId);

        Assert.Equal(expectFallback, classify.GetTagItem(ClassifierTelemetry.Tags.Fallback));
        Assert.Equal(expectReason, classify.GetTagItem(ClassifierTelemetry.Tags.FallbackReason));
        Assert.Equal("fake", classify.GetTagItem(ClassifierTelemetry.Tags.Provider));
        Assert.Equal(expectFallback ? Categories.Other : Categories.Billing, classify.GetTagItem(ClassifierTelemetry.Tags.Category));
    }

    private static Func<IEnumerable<ChatMessage>, CancellationToken, Task<ChatResponse>> Reply(string text) =>
        (_, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));

    private static (ChatClientClassifier Classifier, ListLogger<ChatClientClassifier> Logger) Create(
        Func<IEnumerable<ChatMessage>, CancellationToken, Task<ChatResponse>> respond,
        TimeSpan? timeout = null)
    {
        var logger = new ListLogger<ChatClientClassifier>();
        var classifier = new ChatClientClassifier(
            new FakeChatClient(respond),
            new KeywordClassifier(),
            new ChatClientClassifierOptions("fake", timeout ?? TimeSpan.FromSeconds(5)),
            logger);
        return (classifier, logger);
    }
}

internal sealed class ListLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message, Exception? Exception)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Warnings
    {
        get { lock (_entries) return _entries.Where(e => e.Level == LogLevel.Warning).ToList(); }
    }

    public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> At(LogLevel level)
    {
        lock (_entries) return _entries.Where(e => e.Level == level).ToList();
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_entries) _entries.Add((logLevel, formatter(state, exception), exception));
    }
}
