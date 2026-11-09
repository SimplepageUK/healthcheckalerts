namespace Simplepage.Umbraco.HealthCheckAlerts.Core;

/// <summary>
/// Mutes one health check until a date, for a known problem that is being dealt with. Matched on the
/// check's name, ignoring case. The last muted day is <see cref="Until"/> itself.
/// </summary>
public sealed class Snooze
{
    public string Check { get; set; } = string.Empty;

    /// <summary>yyyy-MM-dd.</summary>
    public string Until { get; set; } = string.Empty;

    public string? Reason { get; set; }

    public DateOnly? UntilDate =>
        DateOnly.TryParseExact(Until, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var date) ? date : null;

    public bool IsValid => !string.IsNullOrWhiteSpace(Check) && UntilDate is not null;

    public bool Covers(string check, DateOnly today) =>
        IsValid && string.Equals(Check.Trim(), check.Trim(), StringComparison.OrdinalIgnoreCase) && today <= UntilDate!.Value;
}
