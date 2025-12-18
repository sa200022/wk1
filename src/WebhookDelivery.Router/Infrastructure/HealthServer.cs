using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WebhookDelivery.Router.Infrastructure;

/// <summary>
/// Lightweight HTTP health endpoint for background service.
/// Listens on configurable port (defaults to 6001).
/// </summary>
public sealed class HealthServer : BackgroundService
{
    private readonly ILogger<HealthServer> _logger;
    private readonly HttpListener _listener = new();
    private readonly int _port;

    public HealthServer(ILogger<HealthServer> logger, int port = 6001)
    {
        _logger = logger;
        _port = port;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _listener.Prefixes.Add($"http://localhost:{_port}/health/");
            _listener.Start();
            stoppingToken.Register(() => _listener.Stop());
            _logger.LogInformation("Router health server listening on port {Port}", _port);
        }
        catch (Exception ex) when (ex is HttpListenerException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning(
                ex,
                "Router health server disabled (failed to bind to http://localhost:{Port}/health/).",
                _port);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (HttpListenerException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            var response = context.Response;
            response.StatusCode = (int)HttpStatusCode.OK;
            await response.OutputStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("ok"));
            response.Close();
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _listener.Stop();
        return base.StopAsync(cancellationToken);
    }
}
