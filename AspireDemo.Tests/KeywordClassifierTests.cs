using AspireDemo.Classifier;

namespace AspireDemo.Tests;

public class KeywordClassifierTests
{
    [Theory]
    [InlineData("my invoice is wrong", "Billing")]
    [InlineData("the charge looks off", "Billing")]
    [InlineData("I can't log in", "Account")]
    [InlineData("password reset please", "Account")]
    [InlineData("I want to cancel", "Cancellation")]
    [InlineData("unsubscribe me", "Cancellation")]
    [InlineData("hello there", "Other")]
    [InlineData("INVOICE", "Billing")]
    public async Task ClassifyAsync_returns_expected_category(string text, string expected)
    {
        var classifier = new KeywordClassifier();

        var category = await classifier.ClassifyAsync(text, TestContext.Current.CancellationToken);

        Assert.Equal(expected, category);
    }
}
