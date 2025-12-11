using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MySql.Data.MySqlClient;
using WebhookDelivery.Core.Repositories;
using WebhookDelivery.DeadLetter.Infrastructure;
using WebhookDelivery.DeadLetter.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register MySQL connection factory
builder.Services.AddScoped(_ =>
{
    var connectionString = builder.Configuration.GetSection("Database:ConnectionString").Value
        ?? throw new InvalidOperationException("Database connection string is not configured");
    return new MySqlConnection(connectionString);
});

// Register repositories
builder.Services.AddScoped<IDeadLetterRepository, MySqlDeadLetterRepository>();
builder.Services.AddScoped<ISagaRepository, MySqlSagaRepository>();
builder.Services.AddScoped<IEventRepository, MySqlEventRepository>();

// Register services
builder.Services.AddScoped<DeadLetterService>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

await app.RunAsync();
