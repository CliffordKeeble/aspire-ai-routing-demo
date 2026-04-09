using AspireDemo.Contracts;
using Aspire.Hosting.ApplicationModel;
using Azure.Messaging.ServiceBus;

namespace AspireDemo.Tests;

public class EndToEndSliceTests
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan EndToEndTimeout = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task Message_flows_Web_to_Classifier_to_Handler()
    {
        var ct = TestContext.Current.CancellationToken;

        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AspireDemo_AppHost>(ct);

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

        var connectionString = await app.GetConnectionStringAsync("messaging", ct)
            ?? throw new InvalidOperationException("messaging connection string was null");
        await using var serviceBus = new ServiceBusClient(connectionString);
        await using var sender = serviceBus.CreateSender(Topics.Incoming);

        var support = new SupportMessage(Guid.NewGuid(), "my invoice is wrong", DateTimeOffset.UtcNow);
        var expectedFragment = $"Handler processed: {support.Id} - Billing";

        var loggerService = app.Services.GetRequiredService<ResourceLoggerService>();

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(EndToEndTimeout);

        // Start watching the handler's log stream BEFORE publishing so we can't miss the line.
        var watchTask = WatchForFragmentAsync(loggerService, "handler", expectedFragment, deadline.Token);

        await sender.SendMessageAsync(new ServiceBusMessage(BinaryData.FromObjectAsJson(support)), ct);

        var found = false;
        try
        {
            found = await watchTask;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Treated as not-found below.
        }

        if (!found)
        {
            var classifierLog = await DumpLogsAsync(loggerService, "classifier", ct);
            var handlerLog = await DumpLogsAsync(loggerService, "handler", ct);
            Assert.Fail(
                $"Did not observe Handler processing log within {EndToEndTimeout.TotalSeconds:F0}s.\n" +
                $"Looking for: \"{expectedFragment}\"\n" +
                $"--- classifier ---\n{classifierLog}\n" +
                $"--- handler ---\n{handlerLog}");
        }
    }

    private static async Task<string> DumpLogsAsync(ResourceLoggerService svc, string name, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            var lines = new List<string>();
            await foreach (var batch in svc.GetAllAsync(name).WithCancellation(cts.Token))
            {
                foreach (var line in batch) lines.Add(line.Content);
                if (lines.Count > 200) break;
            }
            return string.Join('\n', lines.TakeLast(80));
        }
        catch (Exception ex)
        {
            return $"(dump failed: {ex.GetType().Name}: {ex.Message})";
        }
    }

    private static async Task<bool> WatchForFragmentAsync(
        ResourceLoggerService loggerService,
        string resourceName,
        string fragment,
        CancellationToken ct)
    {
        try
        {
            await foreach (var batch in loggerService.WatchAsync(resourceName).WithCancellation(ct))
            {
                foreach (var line in batch)
                {
                    if (line.Content.Contains(fragment, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stream cancelled — fall through to return false.
        }
        return false;
    }
}
