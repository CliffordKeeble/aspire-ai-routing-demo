using AspireDemo.Classifier;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.AddAzureServiceBusClient("messaging");
builder.AddMessageClassifier();
builder.Services.AddHostedService<ClassificationWorker>();

var host = builder.Build();
host.Run();
