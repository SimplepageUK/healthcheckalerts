using Simplepage.Umbraco.HealthCheckAlerts.Core;

namespace Simplepage.Umbraco.HealthCheckAlerts.Services;

/// <summary>Where the dashboard's settings and the last run's state are kept.</summary>
public interface IAlertStore
{
    AlertSettings? LoadSettings();

    void SaveSettings(AlertSettings settings);

    AlertState? LoadState();

    void SaveState(AlertState state);
}
