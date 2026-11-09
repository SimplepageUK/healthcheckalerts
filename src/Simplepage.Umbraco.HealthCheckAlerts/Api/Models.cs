using Simplepage.Umbraco.HealthCheckAlerts.Core;

namespace Simplepage.Umbraco.HealthCheckAlerts.Api;

/// <summary>
/// The settings in force, as the dashboard shows them. The webhook key is never included: only whether
/// one is set. <see cref="Saved"/> names the fields an administrator has saved. The rest come from appsettings.json
/// or the built-in defaults.
/// </summary>
public sealed class SettingsResponse
{
    public string RecipientEmail { get; init; } = string.Empty;
    public string SiteName { get; init; } = string.Empty;
    public string MinimumSeverity { get; init; } = string.Empty;
    public bool SendWhenHealthy { get; init; }
    public string SubjectPrefix { get; init; } = string.Empty;
    public string IgnoreEnvironments { get; init; } = string.Empty;
    public bool ShowEnvironment { get; init; }
    public string WebhookUrl { get; init; } = string.Empty;
    public string WebhookAuthHeader { get; init; } = string.Empty;
    public bool WebhookAuthKeySet { get; init; }
    public string RepeatMode { get; init; } = string.Empty;
    public int ReminderEveryDays { get; init; }
    public bool SendResolved { get; init; }
    public IReadOnlyList<SnoozeModel> Snoozes { get; init; } = [];
    public IReadOnlyList<string> Saved { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Every field is saved as sent. A field that is null or left out is not saved, so the appsettings.json
/// value applies. The exceptions are the webhook key (blank keeps the saved key) and snoozes (left out keeps them).
/// </summary>
public sealed class SettingsRequest
{
    public string? RecipientEmail { get; init; }
    public string? SiteName { get; init; }
    public string? MinimumSeverity { get; init; }
    public bool? SendWhenHealthy { get; init; }
    public string? SubjectPrefix { get; init; }
    public string? IgnoreEnvironments { get; init; }
    public bool? ShowEnvironment { get; init; }
    public string? WebhookUrl { get; init; }
    public string? WebhookAuthHeader { get; init; }

    /// <summary>Write-only. Blank or left out keeps the key that is already saved.</summary>
    public string? WebhookAuthKey { get; init; }

    /// <summary>Removes the saved key, including a key that came from appsettings.json.</summary>
    public bool ClearWebhookAuthKey { get; init; }

    public string? RepeatMode { get; init; }
    public int? ReminderEveryDays { get; init; }
    public bool? SendResolved { get; init; }
    public IReadOnlyList<SnoozeModel>? Snoozes { get; init; }
}

public sealed class SnoozeModel
{
    public string Check { get; init; } = string.Empty;

    /// <summary>yyyy-MM-dd. The last day the problem stays muted.</summary>
    public string Until { get; init; } = string.Empty;

    public string? Reason { get; init; }

    public static SnoozeModel From(Snooze snooze) => new() { Check = snooze.Check, Until = snooze.Until, Reason = snooze.Reason };

    public Snooze ToSnooze() => new() { Check = Check.Trim(), Until = Until.Trim(), Reason = string.IsNullOrWhiteSpace(Reason) ? null : Reason.Trim() };
}

public sealed class StatusResponse
{
    public DateTime? LastRunUtc { get; init; }
    public DateTime? LastSentUtc { get; init; }
    public string? EnvironmentName { get; init; }

    /// <summary>True when this environment is on the ignored list, so scheduled runs here never send.</summary>
    public bool EnvironmentIgnored { get; init; }

    public IReadOnlyList<ProblemModel> Problems { get; init; } = [];
}

public sealed class ProblemModel
{
    public string Check { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? ReadMoreUrl { get; init; }
    public bool IsSnoozed { get; init; }
    public string? SnoozeUntil { get; init; }
    public string? SnoozeReason { get; init; }

    /// <summary>True when the last alert included this exact problem.</summary>
    public bool IsReported { get; init; }
}

public sealed class SnoozeRequest
{
    public string Check { get; init; } = string.Empty;
    public string Until { get; init; } = string.Empty;
    public string? Reason { get; init; }
}

public sealed class TestResponse
{
    public bool Configured { get; init; }
    public bool Delivered { get; init; }
    public IReadOnlyList<string> Messages { get; init; } = [];
}
