using AspireDemo.Contracts;
using AspireDemo.Web.Services;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging.Abstractions;

namespace AspireDemo.Tests;

// The publisher against a fake ServiceBusClient: no emulator.
public class ServiceBusSupportMessagePublisherTests
{
    [Fact]
    public async Task Publishes_to_the_incoming_topic_through_one_sender()
    {
        var client = new FakeClient();
        var publisher = new ServiceBusSupportMessagePublisher(client, NullLogger<ServiceBusSupportMessagePublisher>.Instance);

        await publisher.PublishAsync(Message("first"), TestContext.Current.CancellationToken);
        await publisher.PublishAsync(Message("second"), TestContext.Current.CancellationToken);

        Assert.Equal([Topics.Incoming], client.SendersCreatedFor);
        Assert.Equal(2, client.Sender.Sent.Count);
        Assert.Equal("first", client.Sender.Sent[0].Body.ToObjectFromJson<SupportMessage>()!.CustomerText);
    }

    [Fact]
    public async Task Text_at_the_limit_is_published()
    {
        var client = new FakeClient();
        var publisher = new ServiceBusSupportMessagePublisher(client, NullLogger<ServiceBusSupportMessagePublisher>.Instance);

        await publisher.PublishAsync(
            Message(new string('x', ISupportMessagePublisher.MaxCustomerTextLength)), TestContext.Current.CancellationToken);

        Assert.Single(client.Sender.Sent);
    }

    [Fact]
    public async Task Text_over_the_limit_is_refused_and_nothing_is_sent()
    {
        var client = new FakeClient();
        var publisher = new ServiceBusSupportMessagePublisher(client, NullLogger<ServiceBusSupportMessagePublisher>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() => publisher.PublishAsync(
            Message(new string('x', ISupportMessagePublisher.MaxCustomerTextLength + 1)), TestContext.Current.CancellationToken));

        Assert.Empty(client.Sender.Sent);
    }

    private static SupportMessage Message(string text) => new(Guid.NewGuid(), text, DateTimeOffset.UtcNow);

    private sealed class FakeClient : ServiceBusClient
    {
        public FakeSender Sender { get; } = new();
        public List<string> SendersCreatedFor { get; } = [];

        public override ServiceBusSender CreateSender(string queueOrTopicName)
        {
            SendersCreatedFor.Add(queueOrTopicName);
            return Sender;
        }
    }

    private sealed class FakeSender : ServiceBusSender
    {
        public List<ServiceBusMessage> Sent { get; } = [];

        public override Task SendMessageAsync(ServiceBusMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }
}
