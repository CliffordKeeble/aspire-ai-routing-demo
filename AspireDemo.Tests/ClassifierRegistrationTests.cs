using AspireDemo.Classifier;
using AspireDemo.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace AspireDemo.Tests;

// The classifier's startup rules: what the registration accepts and what stops the host.
public class ClassifierRegistrationTests
{
    [Fact]
    public void Unknown_provider_stops_the_host()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ClassifierTestHost.Build(("Classifier:Provider", "banana")));

        Assert.Contains("banana", ex.Message);
    }

    [Fact]
    public async Task No_provider_set_means_keyword()
    {
        using var probe = new TelemetryProbe();
        using var host = ClassifierTestHost.Build();

        var category = await host.Services.GetRequiredService<IMessageClassifier>()
            .ClassifyAsync("my invoice is wrong", TestContext.Current.CancellationToken);

        Assert.Equal(Categories.Billing, category);
        var count = Assert.Single(probe.Counts("classifier.classifications"));
        Assert.Equal("keyword", count.Tags[ClassifierTelemetry.Tags.Provider]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Openai_without_a_key_stops_the_host(string? key)
    {
        var settings = key is null
            ? new[] { ("Classifier:Provider", "openai") }
            : [("Classifier:Provider", "openai"), ("OpenAI:ApiKey", key)];

        var ex = Assert.Throws<InvalidOperationException>(() => ClassifierTestHost.Build(settings));

        Assert.Contains("OpenAI:ApiKey", ex.Message);
    }

    // ServiceDefaults registers each application's name as a tracing source. If the
    // classifier's application is renamed and this constant is not, its spans are
    // silently no longer exported.
    [Fact]
    public void Telemetry_source_name_is_the_classifier_applications_name()
    {
        Assert.Equal(ClassifierTelemetry.TelemetryName, typeof(ClassifierTelemetry).Assembly.GetName().Name);
    }

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
