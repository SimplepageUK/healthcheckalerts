using System.Text;
using Microsoft.Extensions.Logging;
using Simplepage.Umbraco.HealthCheckAlerts.Core;

namespace Simplepage.Umbraco.HealthCheckAlerts.Services;

public interface IWebhookSender
{
    /// <summary>Posts the JSON. Never throws: a failure comes back as a message that is safe to show.</summary>
    Task<DeliveryResult> SendAsync(HealthAlertOptions options, string json, CancellationToken cancellationToken = default);
}

/// <param name="Message">Safe to log and to show in the dashboard. Never contains the URL or the key.</param>
public sealed record DeliveryResult(bool Ok, string Message);

public sealed class WebhookSender : IWebhookSender
{
    public const string HttpClientName = "Simplepage.HealthCheckAlerts";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly IHttpClientFactory _httpClients;
    private readonly ILogger<WebhookSender> _logger;

    public WebhookSender(IHttpClientFactory httpClients, ILogger<WebhookSender> logger)
    {
        _httpClients = httpClients;
        _logger = logger;
    }

    public async Task<DeliveryResult> SendAsync(HealthAlertOptions options, string json, CancellationToken cancellationToken = default)
    {
        if (options.WebhookUrl is null)
        {
            return new DeliveryResult(false, "No webhook URL is set.");
        }

        // Webhook URLs often carry a secret in the path or query, so only the host is ever reported.
        var host = new Uri(options.WebhookUrl).Host;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);

            using var request = new HttpRequestMessage(HttpMethod.Post, options.WebhookUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            if (!string.IsNullOrEmpty(options.WebhookAuthKey))
            {
                request.Headers.TryAddWithoutValidation(options.WebhookAuthHeader, options.WebhookAuthKey);
            }

            using var client = _httpClients.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, timeout.Token);
            if (response.IsSuccessStatusCode)
            {
                return new DeliveryResult(true, $"Webhook accepted by {host} ({(int)response.StatusCode}).");
            }

            _logger.LogWarning("Health check alerts: webhook to {Host} returned {Status}.", host, (int)response.StatusCode);
            return new DeliveryResult(false, $"Webhook rejected by {host} ({(int)response.StatusCode}).");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Health check alerts: webhook to {Host} timed out.", host);
            return new DeliveryResult(false, $"Webhook to {host} timed out after {Timeout.TotalSeconds:0} seconds.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Health check alerts: webhook to {Host} failed: {Reason}", host, ex.HttpRequestError);
            return new DeliveryResult(false, $"Webhook to {host} failed ({ex.HttpRequestError}).");
        }
    }
}
