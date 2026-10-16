using AspireDemo.Classifier;
using Microsoft.Extensions.DependencyInjection;

namespace AspireDemo.Tests;

// The classifier's startup rules: what the registration accepts and what stops the host.
public class ClassifierRegistrationTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("-30")]
    public void Timeout_of_zero_or_less_stops_the_host(string seconds)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ClassifierTestHost.Build(
            ("Classifier:Provider", "keyword"),
            ("Classifier:TimeoutSeconds", seconds)));

        Assert.Contains("Classifier:TimeoutSeconds", ex.Message);
    }

    [Fact]
    public void A_positive_timeout_is_accepted()
    {
        using var host = ClassifierTestHost.Build(
            ("Classifier:Provider", "keyword"),
            ("Classifier:TimeoutSeconds", "5"));

        Assert.NotNull(host.Services.GetRequiredService<IMessageClassifier>());
    }
}
