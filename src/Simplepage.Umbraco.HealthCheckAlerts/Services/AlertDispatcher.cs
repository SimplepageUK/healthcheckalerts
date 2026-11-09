using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Simplepage.Umbraco.HealthCheckAlerts.Core;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Mail;
using Umbraco.Cms.Core.Models.Email;

namespace Simplepage.Umbraco.HealthCheckAlerts.Services;

/// <param name="Configured">At least one channel (email or webhook) is set up.</param>
/// <param name="Delivered">At least one configured channel accepted the alert.</param>
public sealed record DeliveryReport(bool Configured, bool Delivered, DeliveryResult? Email, DeliveryResult? Webhook);

/// <summary>Sends one outcome down every channel that is set up. A failing channel never blocks the other.</summary>
public sealed class AlertDispatcher
{
    private readonly IEmailSender _emailSender;
    private readonly IOptionsMonitor<ContentSettings> _contentSettings;
    private readonly IWebhookSender _webhook;
    private readonly ILogger<AlertDispatcher> _logger;

    public AlertDispatcher(
        IEmailSender emailSender,
        IOptionsMonitor<ContentSettings> contentSettings,
        IWebhookSender webhook,
        ILogger<AlertDispatcher> logger)
    {
        _emailSender = emailSender;
        _contentSettings = contentSettings;
        _webhook = webhook;
        _logger = logger;
    }

    public async Task<DeliveryReport> DeliverAsync(
        AlertOutcome outcome,
        HealthAlertOptions options,
        HealthAlertContext context,
        CancellationToken cancellationToken = default)
    {
        DeliveryResult? email = null;
        DeliveryResult? webhook = null;

        var planned = HealthAlertPlanner.RenderEmail(outcome, options, context);
        if (planned is not null)
        {
            try
            {
                var message = new EmailMessage(
                    _contentSettings.CurrentValue.Notifications.Email,
                    planned.Recipients.ToArray(),
                    null,
                    null,
                    null,
                    planned.Subject,
                    planned.HtmlBody,
                    true,
                    null);

                // The three-argument overload exists on every 17.x release. The four-argument one (with an
                // expiry) arrived later, and compiling against it would break sites on 17.0 and 17.1.
                await _emailSender.SendAsync(message, Constants.Web.EmailTypes.HealthCheck, false);
                email = new DeliveryResult(true, $"Email sent to {planned.Recipients.Count} recipient(s).");
                _logger.LogInformation(
                    "Health check alerts: sent '{Subject}' to {Recipients} recipient(s).",
                    planned.Subject,
                    planned.Recipients.Count);
            }
            catch (Exception ex)
            {
                email = new DeliveryResult(false, "The email could not be sent. See the log.");
                _logger.LogError(ex, "Health check alerts: the email could not be sent.");
            }
        }

        if (options.WebhookUrl is not null && outcome.Kind != AlertKind.None)
        {
            webhook = await _webhook.SendAsync(options, WebhookPayload.Build(outcome, options, context), cancellationToken);
        }

        var configured = options.Recipients.Count > 0 || options.WebhookUrl is not null;
        return new DeliveryReport(configured, email?.Ok == true || webhook?.Ok == true, email, webhook);
    }
}
