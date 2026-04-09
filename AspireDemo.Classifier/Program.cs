using AspireDemo.Classifier;
using AspireDemo.Contracts;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.AddAzureServiceBusClient("messaging");
builder.Services.AddSingleton<IMessageClassifier, KeywordClassifier>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
