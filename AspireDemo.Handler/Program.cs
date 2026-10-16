using AspireDemo.Handler;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.AddAzureServiceBusClient("messaging");
builder.Services.AddHostedService<ResponseWorker>();

var host = builder.Build();
host.Run();
