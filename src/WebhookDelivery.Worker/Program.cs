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
builder.Logging.AddDebug();

// Register PostgreSQL connection factory
builder.Services.AddScoped(_ =>
{
    var connectionString = builder.Configuration.GetSection("Database:ConnectionString").Value
        ?? throw new InvalidOperationException("Database connection string is not configured");
    return new NpgsqlConnection(connectionString);
});

// Register HTTP client for webhook delivery
builder.Services.AddHttpClient("WebhookClient")
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    });

// Register repositories
builder.Services.AddScoped<IJobRepository, PostgresJobRepository>();
builder.Services.AddScoped<IEventRepository, PostgresEventRepository>();
builder.Services.AddScoped<ISubscriptionRepository, PostgresSubscriptionRepository>();
builder.Services.AddScoped<ISagaRepository, PostgresSagaRepository>();

// Register worker services
builder.Services.AddHostedService<JobWorkerService>();
builder.Services.AddHostedService<LeaseResetCleanerService>();

var host = builder.Build();

await host.RunAsync();
