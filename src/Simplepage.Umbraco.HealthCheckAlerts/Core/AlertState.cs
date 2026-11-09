using System.Text.Json;
using System.Text.Json.Serialization;

namespace Simplepage.Umbraco.HealthCheckAlerts.Core;

/// <summary>
/// What the last scheduled run saw and sent. It is how an unchanged problem is not repeated every day,
/// and how a cleared problem is noticed. The problems also feed the dashboard's snooze list.
/// </summary>
public sealed class AlertState
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public DateTime? LastRunUtc { get; set; }

    /// <summary>When an alert last went out, which the reminder interval counts from.</summary>
    public DateTime? LastSentUtc { get; set; }

    /// <summary>Keys of the problems reported at the last run, after snoozes and the severity filter.</summary>
    public List<string> Known { get; set; } = [];

    /// <summary>Every problem the last run found, before snoozes and the severity filter.</summary>
    public List<HealthCheckProblem> LastProblems { get; set; } = [];

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static AlertState? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var state = JsonSerializer.Deserialize<AlertState>(json, Json);
            if (state is not null)
            {
                state.Known ??= [];
                state.LastProblems ??= [];
            }

            return state;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
