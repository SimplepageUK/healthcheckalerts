namespace Simplepage.Umbraco.HealthCheckAlerts.Core;

public enum AlertKind
{
    /// <summary>Nothing to send.</summary>
    None,

    /// <summary>One or more problems to report.</summary>
    Problems,

    /// <summary>Every problem reported before has cleared.</summary>
    Resolved,

    /// <summary>A run with no problems, for sites that asked to hear that too.</summary>
    AllClear,

    /// <summary>Sent on request from the dashboard to prove delivery works.</summary>
    Test,
}

public sealed record ReportedProblem(HealthCheckProblem Problem, bool IsNew);

/// <param name="ExpiresSoon">The snooze ends within three days, so the problem is about to come back.</param>
public sealed record SnoozedProblem(string Check, DateOnly Until, string? Reason, bool ExpiresSoon);

/// <param name="NewState">What to store once the alert has been delivered. Null means leave the stored state alone.</param>
public sealed record AlertOutcome(
    AlertKind Kind,
    IReadOnlyList<ReportedProblem> Problems,
    IReadOnlyList<string> Resolved,
    IReadOnlyList<SnoozedProblem> Snoozed,
    int Passed,
    int BelowThreshold,
    AlertState? NewState);

/// <summary>
/// Decides what a scheduled run means: which problems count, whether anyone needs telling, and what to
/// remember for next time. Pure, so every rule is unit tested. Email and webhook both render the outcome.
/// </summary>
public static class AlertEngine
{
    private const char KeySeparator = '\u001f';

    public static string Key(HealthCheckProblem problem) =>
        string.Join(KeySeparator, problem.Check, problem.IsError ? "error" : "warning", problem.Message);

    private static string CheckOf(string key) => key.Split(KeySeparator)[0];

    public static AlertOutcome Evaluate(
        IReadOnlyList<HealthCheckProblem> problems,
        int passedChecks,
        HealthAlertOptions options,
        HealthAlertContext context,
        AlertState? state,
        DateTime nowUtc)
    {
        // A forgotten local run must never contact real recipients, and must not disturb the stored state.
        var environment = context.EnvironmentName?.Trim();
        if (!string.IsNullOrEmpty(environment) && options.IgnoreEnvironments.Contains(environment))
        {
            return new AlertOutcome(AlertKind.None, [], [], [], passedChecks, 0, null);
        }

        var today = DateOnly.FromDateTime(context.RunTime);

        var active = new List<HealthCheckProblem>();
        var snoozed = new List<SnoozedProblem>();
        foreach (var problem in problems)
        {
            var snooze = options.Snoozes.FirstOrDefault(s => s.Covers(problem.Check, today));
            if (snooze is null)
            {
                active.Add(problem);
            }
            else if (snoozed.All(s => !string.Equals(s.Check, snooze.Check, StringComparison.OrdinalIgnoreCase)))
            {
                var until = snooze.UntilDate!.Value;
                snoozed.Add(new SnoozedProblem(problem.Check, until, snooze.Reason, until.DayNumber - today.DayNumber <= 3));
            }
        }

        var reportable = options.MinimumSeverity == MinimumSeverity.Error
            ? active.Where(p => p.IsError).ToList()
            : active;
        var belowThreshold = active.Count - reportable.Count;

        var keys = reportable.Select(Key).Distinct().ToList();
        var previous = new HashSet<string>(state?.Known ?? []);
        // A snoozed problem is muted, not fixed, so it must not be announced as cleared.
        var resolvedChecks = previous
            .Where(k => !keys.Contains(k))
            .Select(CheckOf)
            .Where(c => snoozed.All(s => !string.Equals(s.Check, c, StringComparison.OrdinalIgnoreCase)))
            .Distinct()
            .ToList();

        // New problems are only called out once there is something to compare with.
        var newKeys = state is null ? [] : keys.Where(k => !previous.Contains(k)).ToHashSet();

        var ordered = reportable
            .OrderByDescending(p => p.IsError)
            .Select(p => new ReportedProblem(p, newKeys.Contains(Key(p))))
            .ToList();

        var next = new AlertState
        {
            LastRunUtc = nowUtc,
            LastSentUtc = state?.LastSentUtc,
            Known = keys,
            LastProblems = problems.ToList(),
        };

        if (reportable.Count > 0)
        {
            var reminderDue = options.ReminderEveryDays > 0
                              && state?.LastSentUtc is { } lastSent
                              && nowUtc - lastSent >= TimeSpan.FromDays(options.ReminderEveryDays);

            var send = options.RepeatMode == RepeatMode.EveryRun
                       || state?.LastSentUtc is null
                       || newKeys.Count > 0
                       || reminderDue;

            if (send)
            {
                next.LastSentUtc = nowUtc;
                return new AlertOutcome(
                    AlertKind.Problems,
                    ordered,
                    options.SendResolved ? resolvedChecks : [],
                    snoozed,
                    passedChecks,
                    belowThreshold,
                    next);
            }

            return new AlertOutcome(AlertKind.None, ordered, [], snoozed, passedChecks, belowThreshold, next);
        }

        if (resolvedChecks.Count > 0 && options.SendResolved)
        {
            next.LastSentUtc = nowUtc;
            return new AlertOutcome(AlertKind.Resolved, [], resolvedChecks, snoozed, passedChecks, belowThreshold, next);
        }

        if (options.SendWhenHealthy)
        {
            next.LastSentUtc = nowUtc;
            return new AlertOutcome(AlertKind.AllClear, [], [], snoozed, passedChecks, belowThreshold, next);
        }

        return new AlertOutcome(AlertKind.None, [], [], snoozed, passedChecks, belowThreshold, next);
    }

    /// <summary>A made-up run, so the dashboard's test button can prove delivery without waiting for a real problem.</summary>
    public static AlertOutcome Test() => new(
        AlertKind.Test,
        [
            new ReportedProblem(new HealthCheckProblem(
                "Example error", true, "This is a sample error. Nothing is wrong.", "https://docs.umbraco.com/"), true),
            new ReportedProblem(new HealthCheckProblem(
                "Example warning", false, "This is a sample warning. Nothing is wrong."), false),
        ],
        [],
        [],
        0,
        0,
        null);
}
