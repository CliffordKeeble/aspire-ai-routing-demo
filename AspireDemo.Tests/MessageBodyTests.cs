using AspireDemo.Contracts;

namespace AspireDemo.Tests;

public class MessageBodyTests
{
    [Fact]
    public void Valid_json_is_read()
    {
        var id = Guid.NewGuid();
        var body = BinaryData.FromObjectAsJson(new SupportMessage(id, "my invoice is wrong", DateTimeOffset.UtcNow)).ToMemory();

        var ok = MessageBody.TryRead<SupportMessage>(body, out var message, out var failure);

        Assert.True(ok);
        Assert.Equal(id, message!.Id);
        Assert.Null(failure);
    }

    [Fact]
    public void Empty_body_is_a_failure_not_an_exception()
    {
        var ok = MessageBody.TryRead<SupportMessage>(default, out var message, out var failure);

        Assert.False(ok);
        Assert.Null(message);
        Assert.False(string.IsNullOrWhiteSpace(failure));
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    public void Empty_or_json_null_body_is_a_failure(string json)
    {
        var ok = MessageBody.TryRead<SupportMessage>(BinaryData.FromString(json).ToMemory(), out var message, out var failure);

        Assert.False(ok);
        Assert.Null(message);
        Assert.False(string.IsNullOrWhiteSpace(failure));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"Id\": ")]
    [InlineData("{\"Id\": \"not-a-guid\"}")]
    public void Invalid_json_is_a_failure_not_an_exception(string json)
    {
        var ok = MessageBody.TryRead<SupportMessage>(BinaryData.FromString(json).ToMemory(), out var message, out var failure);

        Assert.False(ok);
        Assert.Null(message);
        Assert.False(string.IsNullOrWhiteSpace(failure));
    }
}
