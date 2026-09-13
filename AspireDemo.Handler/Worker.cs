using AspireDemo.Contracts;
using Azure.Messaging.ServiceBus;

namespace AspireDemo.Handler;

public class Worker(ILogger<Worker> logger, ServiceBusClient client) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var processor = client.CreateProcessor(Topics.Classified, Subscriptions.HandlerSub);
        await using var handledSender = client.CreateSender(Topics.Handled);

        processor.ProcessMessageAsync += args => HandleMessageAsync(args, handledSender);
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

    private async Task HandleMessageAsync(ProcessMessageEventArgs args, ServiceBusSender handledSender)
    {
        var message = args.Message.Body.ToObjectFromJson<ClassifiedMessage>()
            ?? throw new InvalidOperationException("Failed to deserialise ClassifiedMessage — body was null.");

        var response = BuildResponse(message.Category);

        logger.LogInformation(
            "Handler processed: {Id} - {Category} - {Response}",
            message.Id, message.Category, response);

        var handled = new HandledMessage(
            message.Id,
            message.Category,
            message.CustomerText,
            response,
            DateTimeOffset.UtcNow);

        await handledSender.SendMessageAsync(
            new ServiceBusMessage(BinaryData.FromObjectAsJson(handled)),
            args.CancellationToken);

        await args.CompleteMessageAsync(args.Message);
    }

    private static string BuildResponse(string category) => category switch
    {
        Categories.Billing => "Thanks — we've routed your billing query to our finance team and will respond within 1 business day.",
        Categories.Account => "We can help with account access. A reset link is on its way to your registered email.",
        Categories.Cancellation => "We're sorry to see you go. A retention specialist will contact you shortly.",
        _ => "Thanks for getting in touch. We'll route this to the right team and respond soon."
    };

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        logger.LogWarning(args.Exception, "Handler processor error. Source={ErrorSource}", args.ErrorSource);
        return Task.CompletedTask;
    }
}
