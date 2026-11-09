using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Simplepage.Umbraco.HealthCheckAlerts.Core;
using Simplepage.Umbraco.HealthCheckAlerts.Services;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
using Umbraco.Cms.Web.Common.Authorization;

namespace Simplepage.Umbraco.HealthCheckAlerts.Api;

/// <summary>
/// The backoffice dashboard's API, at /umbraco/management/api/v1/healthcheckalerts. Administrators only,
/// because it can change who is emailed and where the webhook posts.
/// </summary>
[ApiVersion("1.0")]
[MapToApi(Constants.ApiName)]
[VersionedApiBackOfficeRoute("healthcheckalerts")]
[Authorize(Policy = AuthorizationPolicies.RequireAdminAccess)]
public class HealthCheckAlertsApiController : ManagementApiControllerBase
{
    private static readonly TimeSpan TestInterval = TimeSpan.FromSeconds(10);

    // One test at a time, and not more than one every few seconds, so the button cannot be used to flood anyone.
    private static readonly object TestLock = new();
    private static DateTime _lastTestUtc = DateTime.MinValue;

    private readonly AlertRuntime _runtime;
    private readonly AlertDispatcher _dispatcher;

    public HealthCheckAlertsApiController(AlertRuntime runtime, AlertDispatcher dispatcher)
    {
        _runtime = runtime;
        _dispatcher = dispatcher;
    }

    [HttpGet("settings")]
    [ProducesResponseType<SettingsResponse>(StatusCodes.Status200OK)]
    public IActionResult GetSettings() => Ok(BuildSettingsResponse(_runtime.Store.LoadSettings()));

    [HttpPut("settings")]
    [ProducesResponseType<SettingsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public IActionResult PutSettings([FromBody] SettingsRequest request)
    {
        var existing = _runtime.Store.LoadSettings();

        var settings = new AlertSettings
        {
            RecipientEmail = request.RecipientEmail,
            SiteName = request.SiteName,
            MinimumSeverity = request.MinimumSeverity,
            SendWhenHealthy = request.SendWhenHealthy,
            SubjectPrefix = request.SubjectPrefix,
            IgnoreEnvironments = request.IgnoreEnvironments,
            ShowEnvironment = request.ShowEnvironment,
            WebhookUrl = request.WebhookUrl,
            WebhookAuthHeader = request.WebhookAuthHeader,
            RepeatMode = request.RepeatMode,
            ReminderEveryDays = request.ReminderEveryDays,
            SendResolved = request.SendResolved,
            WebhookAuthKey = request.ClearWebhookAuthKey
                ? string.Empty
                : string.IsNullOrEmpty(request.WebhookAuthKey) ? existing?.WebhookAuthKey : request.WebhookAuthKey,
            Snoozes = request.Snoozes is null
                ? existing?.Snoozes ?? []
                : request.Snoozes.Select(s => s.ToSnooze()).ToList(),
        };
        settings.Snoozes = Dedupe(settings.Snoozes);

        var errors = AlertSettingsValidator.Validate(settings);
        if (errors.Count > 0)
        {
            return Invalid(errors);
        }

        _runtime.Store.SaveSettings(settings);
        return Ok(BuildSettingsResponse(settings));
    }

    [HttpGet("status")]
    [ProducesResponseType<StatusResponse>(StatusCodes.Status200OK)]
    public IActionResult GetStatus()
    {
        var options = _runtime.CurrentOptions();
        var context = _runtime.CreateContext();
        var state = _runtime.Store.LoadState();
        var today = DateOnly.FromDateTime(context.RunTime);
        var reported = new HashSet<string>(state?.Known ?? []);
        var environment = context.EnvironmentName?.Trim();

        var problems = (state?.LastProblems ?? [])
            .OrderByDescending(p => p.IsError)
            .Select(p =>
            {
                var snooze = options.Snoozes.FirstOrDefault(s => s.Covers(p.Check, today));
                return new ProblemModel
                {
                    Check = p.Check,
                    Severity = p.IsError ? "error" : "warning",
                    Message = p.Message,
                    ReadMoreUrl = HealthAlertPlanner.SafeLink(p.ReadMoreLink),
                    IsSnoozed = snooze is not null,
                    SnoozeUntil = snooze?.Until,
                    SnoozeReason = snooze?.Reason,
                    IsReported = reported.Contains(AlertEngine.Key(p)),
                };
            })
            .ToList();

        return Ok(new StatusResponse
        {
            LastRunUtc = Utc(state?.LastRunUtc),
            LastSentUtc = Utc(state?.LastSentUtc),
            EnvironmentName = environment,
            EnvironmentIgnored = !string.IsNullOrEmpty(environment) && options.IgnoreEnvironments.Contains(environment),
            Problems = problems,
        });
    }

    [HttpPost("snooze")]
    [ProducesResponseType<SettingsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public IActionResult Snooze([FromBody] SnoozeRequest request)
    {
        var snooze = new SnoozeModel { Check = request.Check, Until = request.Until, Reason = request.Reason }.ToSnooze();
        var errors = AlertSettingsValidator.ValidateSnooze(snooze);
        if (errors.Count > 0)
        {
            return Invalid(errors);
        }

        var settings = _runtime.Store.LoadSettings() ?? new AlertSettings();
        settings.Snoozes.RemoveAll(s => SameCheck(s.Check, snooze.Check));
        settings.Snoozes.Add(snooze);
        if (settings.Snoozes.Count > AlertSettingsValidator.MaxSnoozes)
        {
            return Invalid([$"There can be at most {AlertSettingsValidator.MaxSnoozes} snoozes."]);
        }

        _runtime.Store.SaveSettings(settings);
        return Ok(BuildSettingsResponse(settings));
    }

    [HttpDelete("snooze")]
    [ProducesResponseType<SettingsResponse>(StatusCodes.Status200OK)]
    public IActionResult Unsnooze([FromQuery] string check)
    {
        var settings = _runtime.Store.LoadSettings();
        if (settings is not null && settings.Snoozes.RemoveAll(s => SameCheck(s.Check, check)) > 0)
        {
            _runtime.Store.SaveSettings(settings);
        }

        return Ok(BuildSettingsResponse(settings));
    }

    [HttpPost("test")]
    [ProducesResponseType<TestResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SendTest(CancellationToken cancellationToken)
    {
        lock (TestLock)
        {
            if (DateTime.UtcNow - _lastTestUtc < TestInterval)
            {
                return StatusCode(StatusCodes.Status429TooManyRequests, new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Wait a few seconds",
                    Detail = "A test alert was only just sent. Try again in ten seconds.",
                });
            }

            _lastTestUtc = DateTime.UtcNow;
        }

        // An explicit request, so it ignores the ignored-environments list. It uses the saved settings.
        var options = _runtime.CurrentOptions();
        var report = await _dispatcher.DeliverAsync(AlertEngine.Test(), options, _runtime.CreateContext(), cancellationToken);

        var messages = new List<string>();
        if (!report.Configured)
        {
            messages.Add("Nothing is set up to send to. Add at least one valid email address or a webhook URL, save, then try again.");
        }

        if (report.Email is not null)
        {
            messages.Add(report.Email.Message);
        }

        if (report.Webhook is not null)
        {
            messages.Add(report.Webhook.Message);
        }

        return Ok(new TestResponse { Configured = report.Configured, Delivered = report.Delivered, Messages = messages });
    }

    private SettingsResponse BuildSettingsResponse(AlertSettings? saved)
    {
        var defaults = _runtime.Defaults();
        var options = AlertSettings.Resolve(defaults, saved);

        // Show addresses and the URL as typed, so a value that was refused is still visible and can be fixed.
        var raw = new Dictionary<string, string>(defaults, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in saved?.ToDictionary() ?? [])
        {
            raw[key] = value;
        }

        return new SettingsResponse
        {
            RecipientEmail = raw.GetValueOrDefault(nameof(AlertSettings.RecipientEmail)) ?? string.Empty,
            SiteName = options.SiteName ?? string.Empty,
            MinimumSeverity = options.MinimumSeverity.ToString(),
            SendWhenHealthy = options.SendWhenHealthy,
            SubjectPrefix = options.SubjectPrefix,
            IgnoreEnvironments = string.Join(", ", options.IgnoreEnvironments.Order(StringComparer.OrdinalIgnoreCase)),
            ShowEnvironment = options.ShowEnvironment,
            WebhookUrl = raw.GetValueOrDefault(nameof(AlertSettings.WebhookUrl)) ?? string.Empty,
            WebhookAuthHeader = options.WebhookAuthHeader,
            WebhookAuthKeySet = options.WebhookAuthKey is not null,
            RepeatMode = options.RepeatMode.ToString(),
            ReminderEveryDays = options.ReminderEveryDays,
            SendResolved = options.SendResolved,
            Snoozes = (saved?.Snoozes ?? []).Select(SnoozeModel.From).ToList(),
            Saved = saved?.ToDictionary().Keys.Where(k => k != nameof(AlertSettings.WebhookAuthKey)).Order().ToList() ?? [],
            Warnings = options.Warnings,
        };
    }

    private ObjectResult Invalid(IEnumerable<string> errors)
    {
        var list = errors.ToList();
        return BadRequest(new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "These settings were not saved",
            Detail = string.Join(" ", list),
            Extensions = { ["errors"] = list },
        });
    }

    private static List<Snooze> Dedupe(IEnumerable<Snooze> snoozes)
    {
        // Last one wins, so re-snoozing a check replaces its date.
        var result = new List<Snooze>();
        foreach (var snooze in snoozes)
        {
            result.RemoveAll(s => SameCheck(s.Check, snooze.Check));
            result.Add(snooze);
        }

        return result;
    }

    private static bool SameCheck(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static DateTime? Utc(DateTime? value) =>
        value is null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
}
