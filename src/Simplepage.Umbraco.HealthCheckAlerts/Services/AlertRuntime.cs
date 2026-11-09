using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using Simplepage.Umbraco.HealthCheckAlerts.Core;
using Umbraco.Cms.Core.Configuration.Models;
using IUmbracoHostingEnvironment = Umbraco.Cms.Core.Hosting.IHostingEnvironment;

namespace Simplepage.Umbraco.HealthCheckAlerts.Services;

/// <summary>
/// Works out the settings in force right now: what an administrator saved in the dashboard, over the
/// defaults in appsettings.json, over the built-in defaults. Read fresh every time, so a saved change
/// applies to the next run with no restart.
/// </summary>
public sealed class AlertRuntime
{
    public const string MethodAlias = "problemsEmail";

    private readonly IAlertStore _store;
    private readonly IOptionsMonitor<HealthChecksSettings> _healthChecks;
    private readonly IUmbracoHostingEnvironment _hosting;
    private readonly IWebHostEnvironment _environment;

    public AlertRuntime(
        IAlertStore store,
        IOptionsMonitor<HealthChecksSettings> healthChecks,
        IUmbracoHostingEnvironment hosting,
        IWebHostEnvironment environment)
    {
        _store = store;
        _healthChecks = healthChecks;
        _hosting = hosting;
        _environment = environment;
    }

    public IAlertStore Store => _store;

    /// <summary>The appsettings.json defaults: the Settings of the problemsEmail notification method.</summary>
    public IDictionary<string, string> Defaults()
    {
        var methods = _healthChecks.CurrentValue.Notification.NotificationMethods;
        foreach (var (alias, method) in methods)
        {
            if (string.Equals(alias, MethodAlias, StringComparison.OrdinalIgnoreCase))
            {
                return method.Settings ?? new Dictionary<string, string>();
            }
        }

        return new Dictionary<string, string>();
    }

    public HealthAlertOptions CurrentOptions() => AlertSettings.Resolve(Defaults(), _store.LoadSettings());

    public HealthAlertContext CreateContext() =>
        // ApplicationMainUrl is only known if UmbracoApplicationUrl is configured or a request has arrived,
        // so it can be null in a scheduled run. The label does not depend on it.
        new(_hosting.ApplicationMainUrl?.Host, _environment.EnvironmentName, DateTime.Now);
}
