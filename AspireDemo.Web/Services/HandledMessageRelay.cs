using AspireDemo.Contracts;
using Azure.Messaging.ServiceBus;

namespace AspireDemo.Web.Services;

// Subscribes to support.handled / webfrontend-sub and pushes each HandledMessage
// into the shared SupportMessageStore. The store raises its event, which the
// Departments Blazor page observes and uses to trigger a re-render.
public sealed class HandledMessageRelay(
    ILogger<HandledMessageRelay> logger,
    ServiceBusClient client,
    SupportMessageStore store) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var processor = client.CreateProcessor(Topics.Handled, Subscriptions.WebFrontend);
        processor.ProcessMessageAsync += HandleMessageAsync;
        processor.ProcessErrorAsync += HandleErrorAsync;

        await processor.StartProcessingAsync(stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await processor.StopProcessingAsync();
            await processor.DisposeAsync();
        }
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        var message = args.Message.Body.ToObjectFromJson<HandledMessage>()
            ?? throw new InvalidOperationException("Failed to deserialise HandledMessage — body was null.");

        store.Add(message);

        logger.LogInformation(
            "Relay received handled message: {Id} - {Category}",
            message.Id, message.Category);

        await args.CompleteMessageAsync(args.Message);
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        logger.LogWarning(args.Exception, "Relay processor error. Source={ErrorSource}", args.ErrorSource);
        return Task.CompletedTask;
    }
}
