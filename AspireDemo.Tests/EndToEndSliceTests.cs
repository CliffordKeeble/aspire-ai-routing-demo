using AspireDemo.Contracts;
using Azure.Messaging.ServiceBus;
using Meziantou.Extensions.Logging.Xunit.v3;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AspireDemo.Tests;

public class EndToEndSliceTests(ITestOutputHelper output)
{
    // Five minutes, not two: on a clean machine the first run pulls the Service Bus
    // emulator and its SQL image before anything can become healthy.
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan EndToEndTimeout = TimeSpan.FromSeconds(60);

    // Publishes straight to support.incoming, so the Web page's own publish path is not
    // on this route (SendMessagePageTests covers it). From there the message crosses the
    // Classifier and the Handler, and the test reads the HandledMessage off support.handled.
    [Fact]
    public async Task Message_published_to_incoming_reaches_the_handled_topic()
    {
        var ct = TestContext.Current.CancellationToken;

        // Keyword mode, set on the AppHost itself: the AppHost then declares no model
        // resource at all, so this suite never pulls an image or waits on a model.
        // ObserveHandled asks the AppHost for a subscription of this test's own on
        // support.handled, so the outcome is read from the topic, not from a log line.
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AspireDemo_AppHost>(
            ["Classifier:Provider=keyword", "Testing:ObserveHandled=true"], ct);
        Assert.DoesNotContain(appHost.Resources, r => r.Name is "ollama" or "classifier-model");

        appHost.Services.AddLogging(b =>
        {
            b.SetMinimumLevel(LogLevel.Information);
            b.AddFilter("AspireDemo.", LogLevel.Information);
            b.AddProvider(new XUnitLoggerProvider(output));
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
        await using var observer = serviceBus.CreateReceiver(Topics.Handled, Subscriptions.TestObserver);

        var support = new SupportMessage(Guid.NewGuid(), "my invoice is wrong", DateTimeOffset.UtcNow);
        await sender.SendMessageAsync(new ServiceBusMessage(BinaryData.FromObjectAsJson(support)), ct);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(EndToEndTimeout);

        HandledMessage? handled = null;
        try
        {
            await foreach (var received in observer.ReceiveMessagesAsync(deadline.Token))
            {
                var candidate = received.Body.ToObjectFromJson<HandledMessage>()
                    ?? throw new InvalidOperationException("A null HandledMessage reached support.handled.");
                await observer.CompleteMessageAsync(received, ct);
                if (candidate.Id == support.Id)
                {
                    handled = candidate;
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Falls through to the failure below, which names what was awaited.
        }

        if (handled is null)
            Assert.Fail($"No HandledMessage with Id {support.Id} reached support.handled within {EndToEndTimeout.TotalSeconds:F0}s.");
        Assert.Equal(Categories.Billing, handled.Category);
        Assert.Equal(support.CustomerText, handled.CustomerText);
    }
}
