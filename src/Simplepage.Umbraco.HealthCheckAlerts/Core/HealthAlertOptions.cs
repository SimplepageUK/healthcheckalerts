namespace Simplepage.Umbraco.HealthCheckAlerts.Core;

/// <summary>How often a problem that has not changed is reported again.</summary>
public enum RepeatMode
{
    /// <summary>Only when a problem is new, with a reminder every ReminderEveryDays while it remains.</summary>
    OnChange,

    /// <summary>On every scheduled run that has a problem.</summary>
    EveryRun,
}

/// <summary>The lowest severity that is worth an email.</summary>
public enum MinimumSeverity
{
    Warning,
    Error,
}

/// <summary>
/// The settings for the problemsEmail notification method, read from
/// Umbraco:CMS:HealthChecks:Notification:NotificationMethods:problemsEmail:Settings.
/// Every value arrives as a string, so parsing and defaults live here where they can be tested.
/// A bad value never throws: it falls back to the default and is reported in <see cref="Warnings"/>
/// so the caller can log it.
/// </summary>
public sealed class HealthAlertOptions
{
    public const string DefaultSubjectPrefix = "Health check:";
    public const string DefaultIgnoredEnvironment = "Development";
    public const string DefaultWebhookAuthHeader = "X-Api-Key";
    public const int DefaultReminderDays = 7;

    private static readonly char[] ListSeparators = [',', ';', ' ', '\t', '\r', '\n'];

    public IReadOnlyList<string> Recipients { get; init; } = [];

    /// <summary>Addresses in RecipientEmail that were not valid and have been skipped.</summary>
    public IReadOnlyList<string> InvalidRecipients { get; init; } = [];

    /// <summary>Null when not set. The planner then falls back to the application URL host.</summary>
    public string? SiteName { get; init; }

    public MinimumSeverity MinimumSeverity { get; init; } = MinimumSeverity.Warning;

    public bool SendWhenHealthy { get; init; }

    public string SubjectPrefix { get; init; } = DefaultSubjectPrefix;

    /// <summary>Environment names that never send, compared ignoring case.</summary>
    public IReadOnlySet<string> IgnoreEnvironments { get; init; } =
        new HashSet<string>([DefaultIgnoredEnvironment], StringComparer.OrdinalIgnoreCase);

    public bool ShowEnvironment { get; init; } = true;

    /// <summary>Null when not set or not usable. Only https, or http to a loopback host for testing.</summary>
    public string? WebhookUrl { get; init; }

    public string WebhookAuthHeader { get; init; } = DefaultWebhookAuthHeader;

    /// <summary>Sent as the value of <see cref="WebhookAuthHeader"/>. Never log or return this.</summary>
    public string? WebhookAuthKey { get; init; }

    public RepeatMode RepeatMode { get; init; } = RepeatMode.OnChange;

    /// <summary>With OnChange, remind about unchanged problems after this many days. 0 means never remind.</summary>
    public int ReminderEveryDays { get; init; } = DefaultReminderDays;

    /// <summary>Tell people when every problem has cleared.</summary>
    public bool SendResolved { get; init; } = true;

    /// <summary>Known problems that are muted until a date.</summary>
    public IReadOnlyList<Snooze> Snoozes { get; init; } = [];

    /// <summary>Problems with the configuration itself, for the caller to log.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public static HealthAlertOptions From(IDictionary<string, string>? settings, IEnumerable<Snooze>? snoozes = null)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (settings is not null)
        {
            foreach (var (key, value) in settings)
            {
                values[key] = value ?? string.Empty;
            }
        }

        var warnings = new List<string>();

        var (recipients, invalid) = RecipientList.Parse(values.GetValueOrDefault("RecipientEmail"));
        if (invalid.Count > 0)
        {
            warnings.Add($"Ignoring {invalid.Count} invalid address(es) in RecipientEmail: {string.Join(", ", invalid)}");
        }

        if (recipients.Count == 0)
        {
            warnings.Add("RecipientEmail has no valid address, so no health check email will be sent.");
        }

        var siteName = values.GetValueOrDefault("SiteName")?.Trim();

        var severity = MinimumSeverity.Warning;
        if (values.TryGetValue("MinimumSeverity", out var severityText) && !string.IsNullOrWhiteSpace(severityText))
        {
            if (Enum.TryParse(severityText.Trim(), ignoreCase: true, out MinimumSeverity parsed)
                && Enum.IsDefined(parsed))
            {
                severity = parsed;
            }
            else
            {
                warnings.Add($"MinimumSeverity '{severityText}' is not 'Warning' or 'Error'. Using 'Warning'.");
            }
        }

        var sendWhenHealthy = ReadBool(values, "SendWhenHealthy", false, warnings);
        var showEnvironment = ReadBool(values, "ShowEnvironment", true, warnings);

        // A missing key means the default. A present but empty key is deliberate: send everywhere.
        var prefix = values.TryGetValue("SubjectPrefix", out var prefixText) ? prefixText.Trim() : DefaultSubjectPrefix;

        IReadOnlySet<string> ignore = values.TryGetValue("IgnoreEnvironments", out var ignoreText)
            ? new HashSet<string>(
                ignoreText.Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>([DefaultIgnoredEnvironment], StringComparer.OrdinalIgnoreCase);

        var webhookUrl = values.GetValueOrDefault("WebhookUrl")?.Trim();
        if (!string.IsNullOrEmpty(webhookUrl) && !IsAllowedWebhookUrl(webhookUrl))
        {
            warnings.Add("WebhookUrl must be an https address (http is only allowed for localhost). The webhook is off.");
            webhookUrl = null;
        }

        var headerName = values.GetValueOrDefault("WebhookAuthHeader")?.Trim();
        if (string.IsNullOrEmpty(headerName))
        {
            headerName = DefaultWebhookAuthHeader;
        }
        else if (!IsHeaderName(headerName))
        {
            warnings.Add($"WebhookAuthHeader '{headerName}' is not a valid header name. Using {DefaultWebhookAuthHeader}.");
            headerName = DefaultWebhookAuthHeader;
        }

        var authKey = values.GetValueOrDefault("WebhookAuthKey");
        if (authKey is not null && authKey.IndexOfAny(['\r', '\n']) >= 0)
        {
            warnings.Add("WebhookAuthKey contains a line break, so it is ignored.");
            authKey = null;
        }

        var repeat = RepeatMode.OnChange;
        if (values.TryGetValue("RepeatMode", out var repeatText) && !string.IsNullOrWhiteSpace(repeatText))
        {
            if (Enum.TryParse(repeatText.Trim(), ignoreCase: true, out RepeatMode parsedRepeat) && Enum.IsDefined(parsedRepeat))
            {
                repeat = parsedRepeat;
            }
            else
            {
                warnings.Add($"RepeatMode '{repeatText}' is not 'OnChange' or 'EveryRun'. Using 'OnChange'.");
            }
        }

        var reminderDays = DefaultReminderDays;
        if (values.TryGetValue("ReminderEveryDays", out var daysText) && !string.IsNullOrWhiteSpace(daysText))
        {
            if (int.TryParse(daysText.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var days) && days <= 365)
            {
                reminderDays = days;
            }
            else
            {
                warnings.Add($"ReminderEveryDays '{daysText}' is not a whole number from 0 to 365. Using {DefaultReminderDays}.");
            }
        }

        var sendResolved = ReadBool(values, "SendResolved", true, warnings);

        return new HealthAlertOptions
        {
            WebhookUrl = string.IsNullOrEmpty(webhookUrl) ? null : webhookUrl,
            WebhookAuthHeader = headerName,
            WebhookAuthKey = string.IsNullOrEmpty(authKey) ? null : authKey,
            RepeatMode = repeat,
            ReminderEveryDays = reminderDays,
            SendResolved = sendResolved,
            Snoozes = (snoozes ?? []).Where(x => x.IsValid).ToList(),
            Recipients = recipients,
            InvalidRecipients = invalid,
            SiteName = string.IsNullOrEmpty(siteName) ? null : siteName,
            MinimumSeverity = severity,
            SendWhenHealthy = sendWhenHealthy,
            SubjectPrefix = prefix,
            IgnoreEnvironments = ignore,
            ShowEnvironment = showEnvironment,
            Warnings = warnings,
        };
    }

    /// <summary>https anywhere, or http only to the local machine so the package can be tested without a certificate.</summary>
    public static bool IsAllowedWebhookUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
    }

    public static bool IsHeaderName(string name) =>
        name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    private static bool ReadBool(Dictionary<string, string> values, string key, bool fallback, List<string> warnings)
    {
        if (!values.TryGetValue(key, out var text) || string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        switch (text.Trim().ToLowerInvariant())
        {
            case "true" or "yes" or "1" or "on":
                return true;
            case "false" or "no" or "0" or "off":
                return false;
            default:
                warnings.Add($"{key} '{text}' is not true or false. Using {fallback.ToString().ToLowerInvariant()}.");
                return fallback;
        }
    }
}
