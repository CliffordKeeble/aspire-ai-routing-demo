using AspireDemo.Contracts;
using Azure.Messaging.ServiceBus;
using System.Text.Json;

namespace AspireDemo.Classifier;

public class Worker(ILogger<Worker> logger, ServiceBusClient client) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var processor = client.CreateProcessor(Topics.Incoming, Subscriptions.ClassifierSub);
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
        var message = args.Message.Body.ToObjectFromJson<SupportMessage>()
            ?? throw new InvalidOperationException("Failed to deserialise SupportMessage — body was null.");
        logger.LogInformation("Classifier received: {Id} - {CustomerText}", message.Id, message.CustomerText);
        await args.CompleteMessageAsync(args.Message);
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        logger.LogWarning(args.Exception, "Classifier processor error. Source={ErrorSource}", args.ErrorSource);
        return Task.CompletedTask;
    }
}
