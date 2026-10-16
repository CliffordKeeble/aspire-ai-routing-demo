using AspireDemo.Contracts;
using Azure.Messaging.ServiceBus;

namespace AspireDemo.Handler;

public partial class Worker(ILogger<Worker> logger, ServiceBusClient client) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var processor = client.CreateProcessor(Topics.Classified, Subscriptions.HandlerSub);
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
            // stoppingToken is already cancelled here. The stop must not be: a cancelled
            // stop returns early and leaves in-flight messages unsettled.
            await processor.StopProcessingAsync(CancellationToken.None);
        }
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args, ServiceBusSender handledSender)
    {
        // A body that will not deserialise will never deserialise: dead-letter it at
        // once rather than let it be redelivered and fail the same way.
        if (!MessageBody.TryRead<ClassifiedMessage>(args.Message.Body.ToMemory(), out var message, out var failure))
        {
            LogPoisonMessage(logger, args.Message.MessageId, args.Message.DeliveryCount, failure);
            await args.DeadLetterMessageAsync(
                args.Message, MessageBody.DeadLetterReason, failure, args.CancellationToken);
            return;
        }

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

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Dead-lettering message {MessageId} (delivery {DeliveryCount}): {Failure}")]
    private static partial void LogPoisonMessage(ILogger logger, string messageId, int deliveryCount, string? failure);
}
