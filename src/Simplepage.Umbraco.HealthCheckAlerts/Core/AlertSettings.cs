using System.Text.Json;
using System.Text.Json.Serialization;

namespace Simplepage.Umbraco.HealthCheckAlerts.Core;

/// <summary>
/// The settings an administrator saves from the backoffice dashboard. A null field means "not set here":
/// the value from appsettings.json (or the built-in default) applies instead. Stored as one JSON document.
/// Parsing and validation stay in <see cref="HealthAlertOptions"/>, so a hand-edited document can never throw.
/// </summary>
public sealed class AlertSettings
{
    public const string FormatVersion = "1";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    /// <summary>One or many addresses separated by commas, semicolons, spaces or new lines.</summary>
    public string? RecipientEmail { get; set; }

    public string? SiteName { get; set; }

    public string? MinimumSeverity { get; set; }

    public bool? SendWhenHealthy { get; set; }

    public string? SubjectPrefix { get; set; }

    public string? IgnoreEnvironments { get; set; }

    public bool? ShowEnvironment { get; set; }

    public string? WebhookUrl { get; set; }

    public string? WebhookAuthHeader { get; set; }

    /// <summary>Write-only through the API: it is stored but never sent back to the browser.</summary>
    public string? WebhookAuthKey { get; set; }

    public string? RepeatMode { get; set; }

    public int? ReminderEveryDays { get; set; }

    public bool? SendResolved { get; set; }

    public List<Snooze> Snoozes { get; set; } = [];

    /// <summary>The settings that are set, as the flat keys <see cref="HealthAlertOptions.From"/> reads.</summary>
    public Dictionary<string, string> ToDictionary()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Put(string key, string? value) { if (value is not null) { d[key] = value; } }
        static string B(bool value) => value ? "true" : "false";

        Put(nameof(RecipientEmail), RecipientEmail);
        Put(nameof(SiteName), SiteName);
        Put(nameof(MinimumSeverity), MinimumSeverity);
        Put(nameof(SendWhenHealthy), SendWhenHealthy is { } a ? B(a) : null);
        Put(nameof(SubjectPrefix), SubjectPrefix);
        Put(nameof(IgnoreEnvironments), IgnoreEnvironments);
        Put(nameof(ShowEnvironment), ShowEnvironment is { } b ? B(b) : null);
        Put(nameof(WebhookUrl), WebhookUrl);
        Put(nameof(WebhookAuthHeader), WebhookAuthHeader);
        Put(nameof(WebhookAuthKey), WebhookAuthKey);
        Put(nameof(RepeatMode), RepeatMode);
        Put(nameof(ReminderEveryDays), ReminderEveryDays?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Put(nameof(SendResolved), SendResolved is { } c ? B(c) : null);
        return d;
    }

    /// <summary>
    /// Saved settings win over the defaults from appsettings.json, field by field. A saved empty string
    /// is a deliberate choice (for example no subject prefix) and wins too.
    /// </summary>
    public static HealthAlertOptions Resolve(IDictionary<string, string>? defaults, AlertSettings? saved)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (defaults is not null)
        {
            foreach (var (key, value) in defaults)
            {
                merged[key] = value ?? string.Empty;
            }
        }

        if (saved is not null)
        {
            foreach (var (key, value) in saved.ToDictionary())
            {
                merged[key] = value;
            }
        }

        return HealthAlertOptions.From(merged, saved?.Snoozes);
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>Returns null for empty or unreadable text, so a damaged value falls back to the defaults.</summary>
    public static AlertSettings? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AlertSettings>(json, Json);
            if (settings is not null)
            {
                settings.Snoozes ??= [];
            }

            return settings;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
