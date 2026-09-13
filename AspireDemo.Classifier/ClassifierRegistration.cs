using System.Data.Common;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenTelemetry.Metrics;

namespace AspireDemo.Classifier;

// Chooses the IMessageClassifier from configuration, never from code.
//   keyword  the default: KeywordClassifier, no network, no model, no cost
//   ollama   a local model via the AppHost's Ollama resource
//   openai   OpenAI, with the key from user secrets or the environment
// Any other value stops the host at startup rather than quietly running keyword.
public static class ClassifierRegistration
{
    public const string ProviderKey = "Classifier:Provider";
    public const string TimeoutSecondsKey = "Classifier:TimeoutSeconds";

    // The AppHost's Ollama model resource name, and so the connection string name.
    public const string OllamaModelConnectionName = "classifier-model";

    public const string OpenAIApiKeyKey = "OpenAI:ApiKey";
    public const string OpenAIModelKey = "OpenAI:Model";
    public const string DefaultOpenAIModel = "gpt-4o-mini";

    public static IHostApplicationBuilder AddMessageClassifier(this IHostApplicationBuilder builder)
    {
        var provider = (builder.Configuration[ProviderKey] ?? "keyword").Trim().ToLowerInvariant();
        var timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue(TimeoutSecondsKey, 30));

        builder.Services.AddSingleton<KeywordClassifier>();
        builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddMeter(ClassifierTelemetry.SourceName));

        switch (provider)
        {
            case "keyword":
                builder.Services.AddSingleton<IMessageClassifier>(sp => sp.GetRequiredService<KeywordClassifier>());
                break;

            case "ollama":
                var (endpoint, model) = ReadOllamaConnection(builder.Configuration);
                AddChatClientClassifier(builder, provider, timeout, new OllamaApiClient(endpoint, model));
                break;

            case "openai":
                var apiKey = builder.Configuration[OpenAIApiKeyKey];
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    throw new InvalidOperationException(
                        $"{ProviderKey} is 'openai' but {OpenAIApiKeyKey} is not set. " +
                        "Set it with user secrets or the environment, never in a file in the repository.");
                }
                var openAIModel = builder.Configuration[OpenAIModelKey] ?? DefaultOpenAIModel;
                AddChatClientClassifier(builder, provider, timeout, new OpenAI.Chat.ChatClient(openAIModel, apiKey).AsIChatClient());
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown {ProviderKey} '{provider}'. Expected 'keyword', 'ollama' or 'openai'.");
        }

        return builder;
    }

    private static void AddChatClientClassifier(
        IHostApplicationBuilder builder, string provider, TimeSpan timeout, IChatClient innerClient)
    {
        builder.Services.AddChatClient(innerClient)
            .UseOpenTelemetry(sourceName: ClassifierTelemetry.SourceName);

        builder.Services.AddSingleton(new ChatClientClassifierOptions(provider, timeout));
        builder.Services.AddSingleton<IMessageClassifier, ChatClientClassifier>();
    }

    private static (Uri Endpoint, string Model) ReadOllamaConnection(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(OllamaModelConnectionName);
        var parts = new DbConnectionStringBuilder { ConnectionString = connectionString ?? string.Empty };

        if (!parts.TryGetValue("Endpoint", out var endpoint) || !parts.TryGetValue("Model", out var model))
        {
            throw new InvalidOperationException(
                $"{ProviderKey} is 'ollama' but connection string '{OllamaModelConnectionName}' is missing or has no " +
                "Endpoint and Model. Run through the AppHost, which declares the Ollama model resource.");
        }

        return (new Uri(endpoint.ToString()!), model.ToString()!);
    }
}
