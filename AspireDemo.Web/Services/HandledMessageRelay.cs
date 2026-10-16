using AspireDemo.Contracts;
using Azure.Messaging.ServiceBus;

namespace AspireDemo.Web.Services;

// Subscribes to support.handled / webfrontend-sub and pushes each HandledMessage
// into the shared HandledMessageStore. The store raises its event, which the
// Departments Blazor page observes and uses to trigger a re-render.
public sealed partial class HandledMessageRelay(
    ILogger<HandledMessageRelay> logger,
    ServiceBusClient client,
    HandledMessageStore store) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var processor = client.CreateProcessor(Topics.Handled, Subscriptions.WebFrontend);
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
            // stoppingToken is already cancelled here. The stop must not be: a cancelled
            // stop returns early and leaves in-flight messages unsettled.
            await processor.StopProcessingAsync(CancellationToken.None);
        }
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        // A body that will not deserialise will never deserialise: dead-letter it at
        // once rather than let it be redelivered and fail the same way.
        if (!MessageBody.TryRead<HandledMessage>(args.Message.Body.ToMemory(), out var message, out var failure))
        {
            LogPoisonMessage(logger, args.Message.MessageId, args.Message.DeliveryCount, failure);
            await args.DeadLetterMessageAsync(
                args.Message, MessageBody.DeadLetterReason, failure, args.CancellationToken);
            return;
        }

        store.Add(message);

        LogReceived(logger, message.Id, message.Category);

        await args.CompleteMessageAsync(args.Message);
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        LogProcessorError(logger, args.Exception, args.ErrorSource);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Relay received handled message: {Id} - {Category}")]
    private static partial void LogReceived(ILogger logger, Guid id, string category);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Relay processor error. Source={ErrorSource}")]
    private static partial void LogProcessorError(ILogger logger, Exception exception, ServiceBusErrorSource errorSource);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Dead-lettering message {MessageId} (delivery {DeliveryCount}): {Failure}")]
    private static partial void LogPoisonMessage(ILogger logger, string messageId, int deliveryCount, string? failure);
}
