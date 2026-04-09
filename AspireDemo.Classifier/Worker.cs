using AspireDemo.Contracts;
using Azure.Messaging.ServiceBus;

namespace AspireDemo.Classifier;

public class Worker(
    ILogger<Worker> logger,
    ServiceBusClient client,
    IMessageClassifier classifier) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var processor = client.CreateProcessor(Topics.Incoming, Subscriptions.ClassifierSub);
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
            await processor.StopProcessingAsync();
            await processor.DisposeAsync();
        }
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args, ServiceBusSender sender)
    {
        var message = args.Message.Body.ToObjectFromJson<SupportMessage>()
            ?? throw new InvalidOperationException("Failed to deserialise SupportMessage — body was null.");

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
}
