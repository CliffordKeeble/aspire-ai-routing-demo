using AspireDemo.Contracts;
using Azure.Messaging.ServiceBus;

namespace AspireDemo.Web.Services;

// One long-lived sender for support.incoming, created once with the singleton,
// rather than a sender opened and closed on every click.
public sealed partial class ServiceBusSupportMessagePublisher : ISupportMessagePublisher, IAsyncDisposable
{
    private readonly ServiceBusSender _sender;
    private readonly ILogger<ServiceBusSupportMessagePublisher> _logger;

    public ServiceBusSupportMessagePublisher(ServiceBusClient client, ILogger<ServiceBusSupportMessagePublisher> logger)
    {
        _sender = client.CreateSender(Topics.Incoming);
        _logger = logger;
    }

    public async Task PublishAsync(SupportMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (string.IsNullOrWhiteSpace(message.CustomerText))
            throw new ArgumentException("Customer text is empty.", nameof(message));
        if (message.CustomerText.Length > ISupportMessagePublisher.MaxCustomerTextLength)
        {
            throw new ArgumentException(
                $"Customer text is {message.CustomerText.Length} characters; the limit is {ISupportMessagePublisher.MaxCustomerTextLength}.",
                nameof(message));
        }

        await _sender.SendMessageAsync(new ServiceBusMessage(BinaryData.FromObjectAsJson(message)), ct);

        LogPublished(_logger, message.Id, Topics.Incoming);
    }

    public ValueTask DisposeAsync() => _sender.DisposeAsync();

    [LoggerMessage(Level = LogLevel.Information, Message = "Published support message {Id} to {Topic}")]
    private static partial void LogPublished(ILogger logger, Guid id, string topic);
}
