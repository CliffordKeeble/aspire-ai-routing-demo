using AspireDemo.Contracts;
using Azure.Messaging.ServiceBus;

namespace AspireDemo.Classifier;

public partial class Worker(
    ILogger<Worker> logger,
    ServiceBusClient client,
    IMessageClassifier classifier) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var processor = client.CreateProcessor(Topics.Incoming, Subscriptions.ClassifierSub);
        await using var sender = client.CreateSender(Topics.Classified);

        processor.ProcessMessageAsync += args => HandleMessageAsync(args, sender);
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

    private async Task HandleMessageAsync(ProcessMessageEventArgs args, ServiceBusSender sender)
    {
        // A body that will not deserialise will never deserialise: dead-letter it at
        // once rather than let it be redelivered and fail the same way.
        if (!MessageBody.TryRead<SupportMessage>(args.Message.Body.ToMemory(), out var message, out var failure))
        {
            LogPoisonMessage(logger, args.Message.MessageId, args.Message.DeliveryCount, failure);
            await args.DeadLetterMessageAsync(
                args.Message, MessageBody.DeadLetterReason, failure, args.CancellationToken);
            return;
        }

        var category = await classifier.ClassifyAsync(message.CustomerText, args.CancellationToken);

        var classified = new ClassifiedMessage(
            message.Id,
            message.CustomerText,
            category,
            DateTimeOffset.UtcNow);

        await sender.SendMessageAsync(
            new ServiceBusMessage(BinaryData.FromObjectAsJson(classified)),
            args.CancellationToken);

        logger.LogInformation("Classifier classified: {Id} - {Category}", classified.Id, classified.Category);
        await args.CompleteMessageAsync(args.Message);
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        logger.LogWarning(args.Exception, "Classifier processor error. Source={ErrorSource}", args.ErrorSource);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Dead-lettering message {MessageId} (delivery {DeliveryCount}): {Failure}")]
    private static partial void LogPoisonMessage(ILogger logger, string messageId, int deliveryCount, string? failure);
}
