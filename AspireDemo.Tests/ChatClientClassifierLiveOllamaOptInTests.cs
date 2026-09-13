using AspireDemo.Classifier;
using AspireDemo.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OllamaSharp;

namespace AspireDemo.Tests;

// OPT-IN. Runs only when ASPIREDEMO_LIVE_OLLAMA_ENDPOINT names a running Ollama
// server (for example the AppHost's ollama container). Skipped everywhere else,
// and says so in the skip reason rather than passing silently.
public class ChatClientClassifierLiveOllamaOptInTests(ITestOutputHelper output)
{
    private const string EndpointVariable = "ASPIREDEMO_LIVE_OLLAMA_ENDPOINT";
    private const string ModelVariable = "ASPIREDEMO_LIVE_OLLAMA_MODEL";

    private static readonly string[] Messages =
    [
        "my invoice is wrong",
        "you charged me twice this month",
        "why is my bill higher than last time?",
        "I can't log in",
        "I forgot my password and the reset email never arrived",
        "my account is locked",
        "I want to cancel my subscription",
        "please close my account, I'm leaving",
        "how do I stop my plan renewing?",
        "do you have an office in Leeds?",
        "your app is great, thanks",
        "the payment page won't let me sign in to pay",
    ];

    [Fact]
    public async Task Live_ollama_reports_the_fallback_rate_and_never_leaves_the_category_set()
    {
        var endpoint = Environment.GetEnvironmentVariable(EndpointVariable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(endpoint), $"Opt-in: set {EndpointVariable} to a running Ollama server.");
        var model = Environment.GetEnvironmentVariable(ModelVariable) ?? "llama3.2:3b";

        using var chatClient = new OllamaApiClient(new Uri(endpoint!), model);
        var logger = new ListLogger<ChatClientClassifier>();
        var classifier = new ChatClientClassifier(
            chatClient, new KeywordClassifier(), new ChatClientClassifierOptions("ollama", TimeSpan.FromSeconds(60)), logger);

        var ct = TestContext.Current.CancellationToken;
        foreach (var message in Messages)
        {
            var before = logger.Warnings.Count;
            var category = await classifier.ClassifyAsync(message, ct);
            var fellBack = logger.Warnings.Count > before;

            output.WriteLine($"{(fellBack ? "FALLBACK" : "model   ")}  {category,-12}  {message}");
            if (fellBack) output.WriteLine($"          {logger.Warnings[^1].Message}");
            Assert.Contains(category, Categories.All);
        }

        output.WriteLine($"model {model}: fallback {logger.Warnings.Count} of {Messages.Length}");
    }

    public enum CategoryChoice { Billing, Account, Cancellation, Other }

    public sealed record CategoryReply(CategoryChoice Category);

    // Investigation, not a guarantee: does a JSON schema bound to an enum survive
    // the OllamaSharp IChatClient path?
    [Fact]
    public async Task Live_ollama_structured_output_probe()
    {
        var endpoint = Environment.GetEnvironmentVariable(EndpointVariable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(endpoint), $"Opt-in: set {EndpointVariable} to a running Ollama server.");
        var model = Environment.GetEnvironmentVariable(ModelVariable) ?? "llama3.2:3b";

        using var chatClient = new OllamaApiClient(new Uri(endpoint!), model);
        var ct = TestContext.Current.CancellationToken;

        var valid = 0;
        foreach (var message in Messages)
        {
            var response = await chatClient.GetResponseAsync<CategoryReply>(
                [new ChatMessage(ChatRole.System, ChatClientClassifier.SystemPrompt), new ChatMessage(ChatRole.User, message)],
                new ChatOptions { Temperature = 0 },
                cancellationToken: ct);

            var ok = response.TryGetResult(out var result);
            if (ok) valid++;
            output.WriteLine($"{(ok ? result!.Category.ToString() : "INVALID"),-12}  raw={response.Text}  <- {message}");
        }

        output.WriteLine($"model {model}: schema-valid {valid} of {Messages.Length}");
    }
}
