using System;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace WebhookDelivery.IntegrationTests;

/// <summary>
/// Base class for integration tests with database setup/teardown
/// </summary>
public abstract class TestBase : IAsyncLifetime
{
    protected string ConnectionString { get; private set; } = null!;
    protected string DatabaseName { get; private set; } = null!;

    public virtual async Task InitializeAsync()
    {
        // Load configuration
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.Test.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var baseConnectionString = config.GetConnectionString("TestDatabase");
        Skip.If(
            string.IsNullOrWhiteSpace(baseConnectionString),
            "Integration tests require PostgreSQL. Set ConnectionStrings__TestDatabase (e.g. Host=localhost;Port=5432;Username=postgres;Password=...;Database=postgres).");

        var baseBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString);

        // Create test database
        await using var connection = new NpgsqlConnection(baseBuilder.ToString());
        try
        {
            await connection.OpenAsync();
        }
        catch (Exception ex)
        {
            Skip.If(true, $"Integration tests skipped: cannot connect to PostgreSQL using ConnectionStrings__TestDatabase ({ex.Message}).");
        }

        DatabaseName = $"webhook_delivery_test_{Guid.NewGuid():N}";
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE \"{DatabaseName}\" WITH TEMPLATE = template1;";
        await cmd.ExecuteNonQueryAsync();

        // Update connection string to use test database
        var testBuilder = new NpgsqlConnectionStringBuilder(baseBuilder.ConnectionString)
        {
            Database = DatabaseName
        };
        ConnectionString = testBuilder.ToString();

        // Run schema migration
        await RunSchemaMigrationAsync();
    }

    public virtual async Task DisposeAsync()
    {
        // Drop test database
        if (!string.IsNullOrEmpty(DatabaseName))
        {
            try
            {
                var baseBuilder = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = "postgres" };
                await using var connection = new NpgsqlConnection(baseBuilder.ToString());
                await connection.OpenAsync();
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = $"DROP DATABASE IF EXISTS \"{DatabaseName}\";";
                await cmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    private async Task RunSchemaMigrationAsync()
    {
        // Locate schema file relative to test bin folder
        var baseDir = AppContext.BaseDirectory;
        var schemaPath = Path.GetFullPath(
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "src", "WebhookDelivery.Database", "Migrations", "001_InitialSchema.sql"));

        if (!System.IO.File.Exists(schemaPath))
            throw new InvalidOperationException($"Schema file not found at {schemaPath}");

        var schemaSql = await System.IO.File.ReadAllTextAsync(schemaPath);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        // Execute entire script (supports multiple statements)
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = schemaSql;
        try
        {
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to apply schema migration from {schemaPath}: {ex.Message}", ex);
        }

        // Verify tables exist
        const string verifySql = @"
            SELECT COUNT(*) FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_name IN ('events','subscriptions','webhook_delivery_sagas','webhook_delivery_jobs','dead_letters');
        ";

        var count = await connection.ExecuteScalarAsync<int>(verifySql);
        if (count < 5)
        {
            throw new InvalidOperationException("Schema migration did not create all required tables (events/subscriptions/sagas/jobs/dead_letters).");
        }
    }

    protected async Task<long> InsertTestEventAsync(
        string eventType = "test.event",
        string payload = "{\"test\":true}",
        string? externalEventId = null,
        DateTime? createdAt = null)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO events (external_event_id, event_type, payload, created_at)
            VALUES (@ExternalEventId, @EventType, @Payload, @CreatedAt)
            RETURNING id;
        ";
        cmd.Parameters.AddWithValue("@ExternalEventId", (object?)externalEventId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@EventType", eventType);
        cmd.Parameters.Add("@Payload", NpgsqlDbType.Jsonb).Value = payload;
        cmd.Parameters.AddWithValue("@CreatedAt", createdAt ?? DateTime.UtcNow);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    protected Task<long> InsertTestEventAsync(string externalEventId, string eventType, JsonDocument payload, DateTime? createdAt = null)
        => InsertTestEventAsync(eventType, payload.RootElement.GetRawText(), externalEventId, createdAt);

    protected async Task<long> InsertTestSubscriptionAsync(
        string eventType = "test.event",
        string callbackUrl = "https://example.com/webhook",
        bool active = true,
        bool verified = true)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO subscriptions (event_type, callback_url, active, verified, created_at, updated_at)
            VALUES (@EventType, @CallbackUrl, @Active, @Verified, NOW(), NOW())
            RETURNING id;
        ";
        cmd.Parameters.AddWithValue("@EventType", eventType);
        cmd.Parameters.AddWithValue("@CallbackUrl", callbackUrl);
        cmd.Parameters.AddWithValue("@Active", active);
        cmd.Parameters.AddWithValue("@Verified", verified);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    protected async Task<long> InsertTestSagaAsync(
        long eventId,
        long subscriptionId,
        string status = "Pending",
        int attemptCount = 0)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO webhook_delivery_sagas
                (event_id, subscription_id, status, attempt_count, next_attempt_at, created_at, updated_at)
            VALUES
                (@EventId, @SubscriptionId, @Status::saga_status_enum, @AttemptCount, NOW(), NOW(), NOW())
            RETURNING id;
        ";
        cmd.Parameters.AddWithValue("@EventId", eventId);
        cmd.Parameters.AddWithValue("@SubscriptionId", subscriptionId);
        cmd.Parameters.AddWithValue("@Status", status);
        cmd.Parameters.AddWithValue("@AttemptCount", attemptCount);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    protected async Task<long> InsertTestJobAsync(
        long sagaId,
        string status = "Pending",
        DateTime? leaseUntil = null,
        DateTime? attemptAt = null,
        int? responseStatus = null,
        string? errorCode = null)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO webhook_delivery_jobs
                (saga_id, status, lease_until, attempt_at, response_status, error_code)
            VALUES
                (@SagaId, @Status::job_status_enum, @LeaseUntil, @AttemptAt, @ResponseStatus, @ErrorCode)
            RETURNING id;
        ";
        cmd.Parameters.AddWithValue("@SagaId", sagaId);
        cmd.Parameters.AddWithValue("@Status", status);
        cmd.Parameters.AddWithValue("@LeaseUntil", (object?)leaseUntil ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@AttemptAt", attemptAt ?? DateTime.UtcNow);
        cmd.Parameters.AddWithValue("@ResponseStatus", (object?)responseStatus ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ErrorCode", (object?)errorCode ?? DBNull.Value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
