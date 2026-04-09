using AspireDemo.Contracts;

var builder = DistributedApplication.CreateBuilder(args);

var cache = builder.AddRedis("cache");

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

builder.AddProject<Projects.AspireDemo_Classifier>("classifier")
    .WithReference(cache)
    .WithReference(messaging)
    // WaitFor is a dev-only orchestration hint. In production, resilience
    // comes from the Service Bus SDK retry policy and worker startup health probes.
    .WaitFor(cache)
    .WaitFor(messaging);

builder.AddProject<Projects.AspireDemo_Handler>("handler")
    .WithReference(cache)
    .WithReference(messaging)
    .WaitFor(cache)
    .WaitFor(messaging);

builder.AddProject<Projects.AspireDemo_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(messaging)
    .WaitFor(messaging);

builder.Build().Run();
