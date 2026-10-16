using AspireDemo.Contracts;

var builder = DistributedApplication.CreateBuilder(args);

// TODO: Redis cache deferred to a future slice. Re-enable when wiring
// the classification cache properly. Costs ~£12/month if provisioned.
// var cache = builder.AddRedis("cache");

var messaging = builder.AddAzureServiceBus("messaging")
    .RunAsEmulator();

// Aspire resource names must be [A-Za-z0-9-]; derive slugs from the constants.
static string Slug(string s) => s.Replace('.', '-');

messaging.AddServiceBusTopic(Slug(Topics.Incoming), Topics.Incoming)
    .AddServiceBusSubscription(Subscriptions.ClassifierSub);

messaging.AddServiceBusTopic(Slug(Topics.Classified), Topics.Classified)
    .AddServiceBusSubscription(Subscriptions.HandlerSub);

messaging.AddServiceBusTopic(Slug(Topics.Handled), Topics.Handled)
    .AddServiceBusSubscription(Subscriptions.WebFrontend);

// keyword (the default when unset), ollama or openai. The launch profiles set
// ollama, so F5 runs a local model; the integration suite sets keyword.
var classifierProvider = (builder.Configuration["Classifier:Provider"] ?? "keyword").Trim().ToLowerInvariant();

var classifier = builder.AddProject<Projects.AspireDemo_Classifier>("classifier")
    // .WithReference(cache)
    .WithReference(messaging)
    // WaitFor is a dev-only orchestration hint. In production, resilience
    // comes from the Service Bus SDK retry policy and worker startup health probes.
    // .WaitFor(cache)
    .WaitFor(messaging)
    .WithEnvironment("Classifier__Provider", classifierProvider);

// The timeout is forwarded as the provider is: the AppHost's configuration is where a
// run is set, and the classifier validates it (it must be greater than 0).
if (builder.Configuration["Classifier:TimeoutSeconds"] is { } classifierTimeoutSeconds)
    classifier.WithEnvironment("Classifier__TimeoutSeconds", classifierTimeoutSeconds);

// Model resources are declared only for the provider that needs them. In keyword
// mode the app model has no Ollama container and no WaitFor on one, so a run
// without a model (the integration suite, CI, a machine with no image cached)
// never pulls or waits. Switching the classifier's registration alone would not
// remove them.
switch (classifierProvider)
{
    case "ollama":
        // The toolkit pulls the model on first start and the named volume keeps it,
        // so only the first run pays for the download.
        var ollama = builder.AddOllama("ollama")
            .WithDataVolume();
        // Resource name is the classifier's connection string name
        // (ClassifierRegistration.OllamaModelConnectionName).
        var model = ollama.AddModel("classifier-model", builder.Configuration["Classifier:OllamaModel"] ?? "llama3.2:3b");
        classifier
            .WithReference(model)
            .WaitFor(model);
        break;

    case "openai":
        // Set with: dotnet user-secrets set "Parameters:openai-api-key" "<key>" (AppHost project)
        var openAIApiKey = builder.AddParameter("openai-api-key", secret: true);
        classifier.WithEnvironment("OpenAI__ApiKey", openAIApiKey);
        if (builder.Configuration["OpenAI:Model"] is { } openAIModel)
            classifier.WithEnvironment("OpenAI__Model", openAIModel);
        break;
}

builder.AddProject<Projects.AspireDemo_Handler>("handler")
    // .WithReference(cache)
    .WithReference(messaging)
    // .WaitFor(cache)
    .WaitFor(messaging);

builder.AddProject<Projects.AspireDemo_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(messaging)
    .WaitFor(messaging);

builder.Build().Run();
