using System.Globalization;
using AspireDemo.Contracts;
using AspireDemo.Web.Components.Pages;
using AspireDemo.Web.Services;
using Bunit;

namespace AspireDemo.Tests;

// The Send Message page against a fake publisher: no Service Bus, no container.
public class SendMessagePageTests : BunitContext
{
    private const string TypedText = "my invoice is wrong";

    [Fact]
    public void Failed_send_shows_an_error_keeps_the_text_and_leaves_the_button_enabled()
    {
        Services.AddSingleton<ISupportMessagePublisher>(new FakePublisher(_ => throw new InvalidOperationException("broker unreachable")));
        var cut = Render<SendMessage>();

        cut.Find("textarea").Change(TypedText);
        SendButton(cut).Click();

        Assert.Contains("could not be sent", cut.Find("[role=alert]").TextContent);
        Assert.Equal(TypedText, cut.Find("textarea").GetAttribute("value"));
        Assert.False(SendButton(cut).HasAttribute("disabled"));
        Assert.DoesNotContain("Sent!", cut.Markup);
    }

    [Fact]
    public void Successful_send_clears_the_text_and_confirms()
    {
        var published = new List<SupportMessage>();
        Services.AddSingleton<ISupportMessagePublisher>(new FakePublisher(published.Add));
        var cut = Render<SendMessage>();

        cut.Find("textarea").Change(TypedText);
        SendButton(cut).Click();

        var message = Assert.Single(published);
        Assert.Equal(TypedText, message.CustomerText);
        Assert.Contains("Sent!", cut.Markup);
        Assert.Empty(cut.FindAll("[role=alert]"));
        Assert.False(SendButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public void Textarea_limits_its_length_to_the_publishers_maximum()
    {
        Services.AddSingleton<ISupportMessagePublisher>(new FakePublisher(_ => { }));
        var cut = Render<SendMessage>();

        Assert.Equal(
            ISupportMessagePublisher.MaxCustomerTextLength.ToString(CultureInfo.InvariantCulture),
            cut.Find("textarea").GetAttribute("maxlength"));
    }

    private static AngleSharp.Dom.IElement SendButton(IRenderedComponent<SendMessage> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Send");

    private sealed class FakePublisher(Action<SupportMessage> onPublish) : ISupportMessagePublisher
    {
        public Task PublishAsync(SupportMessage message, CancellationToken ct = default)
        {
            onPublish(message);
            return Task.CompletedTask;
        }
    }
}
