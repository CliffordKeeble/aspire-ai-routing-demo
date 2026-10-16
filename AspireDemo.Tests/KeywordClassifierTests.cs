using AspireDemo.Classifier;
using AspireDemo.Contracts;

namespace AspireDemo.Tests;

public class KeywordClassifierTests
{
    [Theory]
    [InlineData("my invoice is wrong", Categories.Billing)]
    [InlineData("the charge looks off", Categories.Billing)]
    [InlineData("I can't log in", Categories.Account)]
    [InlineData("password reset please", Categories.Account)]
    [InlineData("I want to cancel", Categories.Cancellation)]
    [InlineData("unsubscribe me", Categories.Cancellation)]
    [InlineData("hello there", Categories.Other)]
    [InlineData("INVOICE", Categories.Billing)]
    public async Task Keyword_matcher_maps_text_to_the_expected_category(string text, string expected)
    {
        var classifier = new KeywordClassifier();

        var category = await classifier.ClassifyAsync(text, TestContext.Current.CancellationToken);

        Assert.Equal(expected, category);
    }

    // What the matcher does today, pinned so a change is deliberate. Neither case is
    // endorsed: the first is a customer cancelling, the second is a phone charger.
    [Theory]
    [InlineData("cancel my bill", Categories.Billing)]    // precedence: Billing's words are tried before Cancellation's
    [InlineData("charger", Categories.Billing)]           // substring: "charge" matches inside "charger"
    public async Task Keyword_matcher_precedence_and_substring_behaviour_is_pinned(string text, string expected)
    {
        var classifier = new KeywordClassifier();

        var category = await classifier.ClassifyAsync(text, TestContext.Current.CancellationToken);

        Assert.Equal(expected, category);
    }
}
