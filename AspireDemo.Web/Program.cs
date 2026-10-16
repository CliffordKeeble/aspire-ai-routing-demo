using AspireDemo.Web;
using AspireDemo.Web.Components;
using AspireDemo.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();
// TODO: Redis cache deferred to a future slice. Re-enable when wiring
// the classification cache properly.
// builder.AddRedisOutputCache("cache");
builder.AddAzureServiceBusClient("messaging");

// Live department view: shared singleton state + background relay from Service Bus.
builder.Services.AddSingleton<HandledMessageStore>();
builder.Services.AddSingleton<ISupportMessagePublisher, ServiceBusSupportMessagePublisher>();
builder.Services.AddHostedService<HandledMessageRelay>();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAntiforgery();

// app.UseOutputCache(); // Redis cache deferred

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();
