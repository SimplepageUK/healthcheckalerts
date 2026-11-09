using System.Globalization;
using System.Text.Json;

namespace Simplepage.Umbraco.HealthCheckAlerts.Core;

/// <summary>
/// The JSON body posted to the webhook. Deliberately flat and stable so anything can read it:
/// Zapier, Make, an Azure Function, or a few lines of code. The auth key is never part of it.
/// </summary>
public static class WebhookPayload
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public static string StatusName(AlertKind kind) => kind switch
    {
        AlertKind.Problems => "problems",
        AlertKind.Resolved => "resolved",
        AlertKind.AllClear => "all-clear",
        AlertKind.Test => "test",
        _ => "none",
    };

    public static string Build(AlertOutcome outcome, HealthAlertOptions options, HealthAlertContext context)
    {
        var errors = outcome.Problems.Count(p => p.Problem.IsError);
        var payload = new
        {
            source = "Simplepage.Umbraco.HealthCheckAlerts",
            status = StatusName(outcome.Kind),
            summary = HealthAlertPlanner.Summary(outcome),
            site = HealthAlertPlanner.Label(options, context),
            siteName = string.IsNullOrWhiteSpace(options.SiteName) ? null : options.SiteName,
            environment = context.EnvironmentName,
            host = context.Host,
            runAt = context.RunTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            errors,
            warnings = outcome.Problems.Count - errors,
            passedChecks = outcome.Passed,
            problems = outcome.Problems.Select(p => new
            {
                check = p.Problem.Check,
                severity = p.Problem.IsError ? "error" : "warning",
                message = p.Problem.Message,
                readMoreUrl = HealthAlertPlanner.SafeLink(p.Problem.ReadMoreLink),
                isNew = p.IsNew,
            }),
            resolved = outcome.Resolved,
            snoozed = outcome.Snoozed.Select(s => new
            {
                check = s.Check,
                until = s.Until.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                reason = s.Reason,
            }),
        };
        return JsonSerializer.Serialize(payload, Json);
    }
}
