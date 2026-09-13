using System.Collections.Concurrent;
using AspireDemo.Contracts;
using Azure.Messaging.ServiceBus;
using Meziantou.Extensions.Logging.Xunit.v3;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AspireDemo.Tests;

public class EndToEndSliceTests(ITestOutputHelper output)
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan EndToEndTimeout = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task Message_flows_Web_to_Classifier_to_Handler_to_Relay()
    {
        var ct = TestContext.Current.CancellationToken;

        var capture = new CapturingLoggerProvider();

        // Keyword mode, set on the AppHost itself: the AppHost then declares no model
        // resource at all, so this suite never pulls an image or waits on a model.
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AspireDemo_AppHost>(
            ["Classifier:Provider=keyword"], ct);
        Assert.DoesNotContain(appHost.Resources, r => r.Name is "ollama" or "classifier-model");

        appHost.Services.AddLogging(b =>
        {
            b.SetMinimumLevel(LogLevel.Information);
            b.AddFilter("AspireDemo.", LogLevel.Information);
            b.AddProvider(new XUnitLoggerProvider(output));
            b.AddProvider(capture);
        });

        await using var app = await appHost.BuildAsync(ct).WaitAsync(StartupTimeout, ct);
        await app.StartAsync(ct).WaitAsync(StartupTimeout, ct);

        await app.ResourceNotifications
            .WaitForResourceHealthyAsync("messaging", ct)
            .WaitAsync(StartupTimeout, ct);
        await app.ResourceNotifications
            .WaitForResourceHealthyAsync("classifier", ct)
            .WaitAsync(StartupTimeout, ct);
        await app.ResourceNotifications
            .WaitForResourceHealthyAsync("handler", ct)
            .WaitAsync(StartupTimeout, ct);
        await app.ResourceNotifications
            .WaitForResourceHealthyAsync("webfrontend", ct)
            .WaitAsync(StartupTimeout, ct);

        var connectionString = await app.GetConnectionStringAsync("messaging", ct)
            ?? throw new InvalidOperationException("messaging connection string was null");
        await using var serviceBus = new ServiceBusClient(connectionString);
        await using var sender = serviceBus.CreateSender(Topics.Incoming);

        var support = new SupportMessage(Guid.NewGuid(), "my invoice is wrong", DateTimeOffset.UtcNow);
        // Looking for the Web relay log line proves all four hops fired:
        // Web publish -> Classifier consume+publish -> Handler consume+publish -> Web relay consume.
        var expectedFragment = $"Relay received handled message: {support.Id} - Billing";

        await sender.SendMessageAsync(new ServiceBusMessage(BinaryData.FromObjectAsJson(support)), ct);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(EndToEndTimeout);

        var found = false;
        while (!deadline.IsCancellationRequested)
        {
            if (capture.ContainsLineMatching(expectedFragment))
            {
                found = true;
                break;
            }
            try { await Task.Delay(250, deadline.Token); }
            catch (OperationCanceledException) { break; }
        }

        if (!found)
        {
            Assert.Fail(
                $"Did not observe relay log within {EndToEndTimeout.TotalSeconds:F0}s.\n" +
                $"Looking for: \"{expectedFragment}\"\n" +
                $"--- captured tail ---\n" +
                string.Join('\n', capture.Tail(60)));
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName, _lines);
        public void Dispose() { }

        public bool ContainsLineMatching(string fragment) =>
            _lines.Any(l => l.Contains(fragment, StringComparison.Ordinal));

        public IReadOnlyCollection<string> Tail(int n) => _lines.ToArray().TakeLast(n).ToArray();

        private sealed class CaptureLogger(string category, ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lines.Enqueue($"[{category}] {formatter(state, exception)}");
            }
        }
    }
}
