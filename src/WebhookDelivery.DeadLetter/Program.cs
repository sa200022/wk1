using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using WebhookDelivery.Core.Repositories;
using WebhookDelivery.DeadLetter.Infrastructure;
using WebhookDelivery.DeadLetter.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register PostgreSQL connection factory
builder.Services.AddScoped(_ =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Database connection string is not configured");
    return new NpgsqlConnection(connectionString);
});

// Register repositories
builder.Services.AddScoped<IDeadLetterRepository, PostgresDeadLetterRepository>();
builder.Services.AddScoped<ISagaRepository, PostgresSagaRepository>();
builder.Services.AddScoped<IEventRepository, PostgresEventRepository>();

// Register services
builder.Services.AddScoped<DeadLetterService>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

var apiKey = app.Configuration["Security:ApiKey"];
if (!string.IsNullOrWhiteSpace(apiKey))
{
    app.Use(async (context, next) =>
    {
        if (!context.Request.Headers.TryGetValue("X-Api-Key", out var provided) ||
            provided != apiKey)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized");
            return;
        }

        await next();
    });
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

await app.RunAsync();
