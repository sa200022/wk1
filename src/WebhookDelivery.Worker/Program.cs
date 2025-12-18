using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebhookDelivery.Core.Repositories;
using WebhookDelivery.Worker.Infrastructure;
using WebhookDelivery.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddJsonConsole();
builder.Logging.AddDebug();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Database connection string is not configured");

// Register HTTP client for webhook delivery
var httpTimeoutSeconds = builder.Configuration.GetValue<int>("Worker:HttpTimeoutSeconds", 30);
builder.Services.AddHttpClient("WebhookClient")
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(httpTimeoutSeconds);
    });

// Register repositories
builder.Services.AddSingleton<IJobRepository>(_ => new PostgresJobRepository(connectionString));
builder.Services.AddSingleton<IEventRepository>(_ => new PostgresEventRepository(connectionString));
builder.Services.AddSingleton<ISubscriptionRepository>(_ => new PostgresSubscriptionRepository(connectionString));
builder.Services.AddSingleton<ISagaRepository>(_ => new PostgresSagaRepository(connectionString));

// Register worker services
builder.Services.AddHostedService<JobWorkerService>();
builder.Services.AddHostedService<LeaseResetCleanerService>();
builder.Services.AddHostedService(provider =>
{
    var logger = provider.GetRequiredService<ILogger<HealthServer>>();
    var port = builder.Configuration.GetValue<int>("Health:Port", 6003);
    return new HealthServer(logger, port);
});

var host = builder.Build();

await host.RunAsync();
