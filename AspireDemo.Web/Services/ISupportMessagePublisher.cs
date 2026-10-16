using AspireDemo.Contracts;

namespace AspireDemo.Web.Services;

// The first hop: Web publishes a customer's message to support.incoming.
public interface ISupportMessagePublisher
{
    // The longest customer text the page will accept, and the publisher will send.
    // The textarea's maxlength reads this too, so the two cannot drift.
    public const int MaxCustomerTextLength = 2000;

    Task PublishAsync(SupportMessage message, CancellationToken ct = default);
}
