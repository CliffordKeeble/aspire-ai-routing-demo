using AspireDemo.Classifier;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.AddAzureServiceBusClient("messaging");
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
