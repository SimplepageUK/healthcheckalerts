using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Simplepage.Umbraco.HealthCheckAlerts.Core;
using Simplepage.Umbraco.HealthCheckAlerts.Services;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Cms.Core.HealthChecks.NotificationMethods;

namespace Simplepage.Umbraco.HealthCheckAlerts.Notifications;

/// <summary>
/// Reports only the problems from Umbraco's scheduled health checks, by email and optionally a webhook.
/// It names the site and environment, mutes snoozed problems, does not repeat a problem that has not
/// changed, and says when everything has cleared. Umbraco finds this class by scanning for health check
/// notification methods, so there is nothing to register.
/// Switch it on under Umbraco:CMS:HealthChecks:Notification:NotificationMethods:problemsEmail. Everything
/// else is edited in the backoffice (Settings > Health Check Alerts) and applies from the next run, with
/// no deploy. Values in that appsettings entry are the defaults until something is saved there.
/// The FailureOnly and Verbosity settings that Umbraco's own methods use are ignored here.
/// </summary>
[HealthCheckNotificationMethod(Alias)]
public class ProblemsEmailNotificationMethod : NotificationMethodBase
{
    /// <summary>The key to use under NotificationMethods in appsettings.json.</summary>
    public const string Alias = AlertRuntime.MethodAlias;

    private readonly AlertRuntime _runtime;
    private readonly AlertDispatcher _dispatcher;
    private readonly ILogger<ProblemsEmailNotificationMethod> _logger;

    public ProblemsEmailNotificationMethod(
        IOptionsMonitor<HealthChecksSettings> healthChecksSettings,
        AlertRuntime runtime,
        AlertDispatcher dispatcher,
        ILogger<ProblemsEmailNotificationMethod> logger)
        : base(healthChecksSettings)
    {
        _runtime = runtime;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public override async Task SendAsync(HealthCheckResults results)
    {
        if (!Enabled)
        {
            return;
        }

        var options = _runtime.CurrentOptions();
        foreach (var warning in options.Warnings)
        {
            _logger.LogWarning("Health check alerts: {Warning}", warning);
        }

        var (problems, passedChecks) = ProblemCollector.Collect(results);
        var context = _runtime.CreateContext();
        var nowUtc = DateTime.UtcNow;

        var previous = _runtime.Store.LoadState();
        var outcome = AlertEngine.Evaluate(problems, passedChecks, options, context, previous, nowUtc);

        if (outcome.Kind == AlertKind.None)
        {
            _logger.LogInformation(
                "Health check alerts: nothing to send ({Problems} problem(s) found, environment {Environment}).",
                problems.Count,
                context.EnvironmentName);
            Remember(outcome.NewState);
            return;
        }

        var report = await _dispatcher.DeliverAsync(outcome, options, context);
        if (!report.Configured)
        {
            _logger.LogWarning("Health check alerts: no valid recipient and no webhook, so nothing was sent.");
        }

        if (outcome.NewState is null)
        {
            return;
        }

        if (report.Delivered)
        {
            Remember(outcome.NewState);
        }
        else
        {
            // Nothing got through, so keep the old record of what was reported and try again next run.
            // The snapshot of what this run found is still worth keeping for the dashboard.
            Remember(new AlertState
            {
                LastRunUtc = outcome.NewState.LastRunUtc,
                LastSentUtc = previous?.LastSentUtc,
                Known = previous?.Known ?? [],
                LastProblems = outcome.NewState.LastProblems,
            });
        }
    }

    private void Remember(AlertState? state)
    {
        if (state is null)
        {
            return;
        }

        try
        {
            _runtime.Store.SaveState(state);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check alerts: could not save the state of this run.");
        }
    }
}
