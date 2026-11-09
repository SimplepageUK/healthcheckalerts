namespace Simplepage.Umbraco.HealthCheckAlerts.Core;

/// <summary>
/// Checks what an administrator is about to save. <see cref="HealthAlertOptions"/> never throws and quietly
/// falls back, which is right for a scheduled run but wrong for a form: here a bad value must be refused
/// with a clear message, so nothing is saved that will later be ignored.
/// </summary>
public static class AlertSettingsValidator
{
    public const int MaxSnoozes = 100;
    private const int MaxText = 200;
    private const int MaxKey = 1000;

    /// <summary>Returns one plain-English message per problem. Empty means the settings are fine to save.</summary>
    public static IReadOnlyList<string> Validate(AlertSettings settings)
    {
        var errors = new List<string>();

        if (!string.IsNullOrWhiteSpace(settings.RecipientEmail))
        {
            var (_, invalid) = RecipientList.Parse(settings.RecipientEmail);
            if (invalid.Count > 0)
            {
                errors.Add($"These are not valid email addresses: {string.Join(", ", invalid)}.");
            }
        }

        TooLong(errors, "Site name", settings.SiteName, MaxText);
        TooLong(errors, "Subject prefix", settings.SubjectPrefix, MaxText);
        TooLong(errors, "Ignored environments", settings.IgnoreEnvironments, MaxText);

        if (!string.IsNullOrWhiteSpace(settings.MinimumSeverity)
            && !(Enum.TryParse(settings.MinimumSeverity.Trim(), true, out MinimumSeverity severity) && Enum.IsDefined(severity)))
        {
            errors.Add("Minimum severity must be Warning or Error.");
        }

        if (!string.IsNullOrWhiteSpace(settings.RepeatMode)
            && !(Enum.TryParse(settings.RepeatMode.Trim(), true, out RepeatMode mode) && Enum.IsDefined(mode)))
        {
            errors.Add("Repeat mode must be OnChange or EveryRun.");
        }

        if (settings.ReminderEveryDays is < 0 or > 365)
        {
            errors.Add("Reminder interval must be a whole number of days from 0 to 365.");
        }

        if (!string.IsNullOrWhiteSpace(settings.WebhookUrl) && !HealthAlertOptions.IsAllowedWebhookUrl(settings.WebhookUrl.Trim()))
        {
            errors.Add("The webhook URL must be an https address with no username or password. http is only allowed for localhost.");
        }

        if (!string.IsNullOrWhiteSpace(settings.WebhookAuthHeader) && !HealthAlertOptions.IsHeaderName(settings.WebhookAuthHeader.Trim()))
        {
            errors.Add("The webhook header name can only contain letters, numbers and hyphens.");
        }

        if (settings.WebhookAuthKey is { } key)
        {
            if (key.IndexOfAny(['\r', '\n']) >= 0)
            {
                errors.Add("The webhook key cannot contain a line break.");
            }

            if (key.Length > MaxKey)
            {
                errors.Add($"The webhook key is longer than {MaxKey} characters.");
            }
        }

        if (settings.Snoozes.Count > MaxSnoozes)
        {
            errors.Add($"There can be at most {MaxSnoozes} snoozes.");
        }

        foreach (var snooze in settings.Snoozes)
        {
            errors.AddRange(ValidateSnooze(snooze));
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateSnooze(Snooze snooze)
    {
        var errors = new List<string>();
        var name = string.IsNullOrWhiteSpace(snooze.Check) ? "a snooze" : $"the snooze for '{snooze.Check.Trim()}'";
        if (string.IsNullOrWhiteSpace(snooze.Check))
        {
            errors.Add("A snooze needs the name of the check.");
        }
        else if (snooze.Check.Length > MaxText)
        {
            errors.Add("A check name is longer than 200 characters.");
        }

        if (snooze.UntilDate is null)
        {
            errors.Add($"The date for {name} must be written yyyy-MM-dd.");
        }

        if (snooze.Reason is { Length: > MaxText })
        {
            errors.Add($"The reason for {name} is longer than {MaxText} characters.");
        }

        return errors;
    }

    private static void TooLong(List<string> errors, string field, string? value, int max)
    {
        if (value is not null && value.Length > max)
        {
            errors.Add($"{field} is longer than {max} characters.");
        }
    }
}
