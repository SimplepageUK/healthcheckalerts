using Microsoft.Extensions.DependencyInjection;
using Simplepage.Umbraco.HealthCheckAlerts.Core;
using Umbraco.Cms.Core.Services;

namespace Simplepage.Umbraco.HealthCheckAlerts.Services;

/// <summary>
/// Keeps settings and state in Umbraco's own key/value table, so there is no schema to migrate, nothing
/// to deploy, and it works on Umbraco Cloud. The service is resolved per call so it does not matter whether
/// Umbraco registers it as a singleton or scoped.
/// </summary>
public sealed class KeyValueAlertStore : IAlertStore
{
    public const string SettingsKey = "Simplepage.HealthCheckAlerts.Settings";
    public const string StateKey = "Simplepage.HealthCheckAlerts.State";

    private readonly IServiceScopeFactory _scopes;

    public KeyValueAlertStore(IServiceScopeFactory scopes) => _scopes = scopes;

    public AlertSettings? LoadSettings() => AlertSettings.FromJson(Get(SettingsKey));

    public void SaveSettings(AlertSettings settings) => Set(SettingsKey, settings.ToJson());

    public AlertState? LoadState() => AlertState.FromJson(Get(StateKey));

    public void SaveState(AlertState state) => Set(StateKey, state.ToJson());

    private string? Get(string key)
    {
        using var scope = _scopes.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IKeyValueService>().GetValue(key);
    }

    private void Set(string key, string value)
    {
        using var scope = _scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<IKeyValueService>().SetValue(key, value);
    }
}
