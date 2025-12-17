using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using WebhookDelivery.Core.Repositories;
using WebhookDelivery.Worker.Infrastructure;
using WebhookDelivery.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddJsonConsole();
builder.Logging.AddDebug();

// Register PostgreSQL connection factory
builder.Services.AddScoped(_ =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Database connection string is not configured");
    return new NpgsqlConnection(connectionString);
});

// Register HTTP client for webhook delivery
var httpTimeoutSeconds = builder.Configuration.GetValue<int>("Worker:HttpTimeoutSeconds", 30);
builder.Services.AddHttpClient("WebhookClient")
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(httpTimeoutSeconds);
    });

// Register repositories
builder.Services.AddScoped<IJobRepository, PostgresJobRepository>();
builder.Services.AddScoped<IEventRepository, PostgresEventRepository>();
builder.Services.AddScoped<ISubscriptionRepository, PostgresSubscriptionRepository>();
builder.Services.AddScoped<ISagaRepository, PostgresSagaRepository>();

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
