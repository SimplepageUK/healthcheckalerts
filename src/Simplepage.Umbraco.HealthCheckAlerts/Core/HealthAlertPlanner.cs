using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Simplepage.Umbraco.HealthCheckAlerts.Core;

/// <summary>Where and when a run happened. Supplied by the caller so the planner stays pure.</summary>
/// <param name="Host">The application URL host, or null when Umbraco does not know it.</param>
/// <param name="EnvironmentName">The ASP.NET Core environment, for example Production.</param>
/// <param name="RunTime">When the run happened, shown in the body.</param>
public sealed record HealthAlertContext(string? Host, string? EnvironmentName, DateTime RunTime);

/// <summary>An email ready to hand to Umbraco's email sender.</summary>
public sealed record PlannedAlert(string Subject, string HtmlBody, IReadOnlyList<string> Recipients);

/// <summary>
/// Decides whether a run is worth an email and, if so, what it says. No Umbraco, no ASP.NET Core,
/// no clock and no I/O, so every rule here is covered by plain unit tests.
/// </summary>
public static class HealthAlertPlanner
{
    private static readonly Regex Whitespace = new(@"[\s\p{C}]+", RegexOptions.Compiled);

    /// <summary>
    /// The stateless rules: every problem counts as new and nothing is remembered. Returns null when
    /// nothing should be sent. The scheduled method uses <see cref="AlertEngine"/> and
    /// <see cref="RenderEmail"/> directly so it can remember what it has already reported.
    /// </summary>
    public static PlannedAlert? Plan(
        IReadOnlyList<HealthCheckProblem> problems,
        int passedChecks,
        HealthAlertOptions options,
        HealthAlertContext context)
    {
        var outcome = AlertEngine.Evaluate(problems, passedChecks, options, context, state: null, DateTime.UtcNow);
        return RenderEmail(outcome, options, context);
    }

    /// <summary>The email for an outcome, or null when there is nothing to say or nobody to say it to.</summary>
    public static PlannedAlert? RenderEmail(AlertOutcome outcome, HealthAlertOptions options, HealthAlertContext context)
    {
        if (outcome.Kind == AlertKind.None || options.Recipients.Count == 0)
        {
            return null;
        }

        var label = Label(options, context);
        var summary = Summary(outcome);
        return new PlannedAlert(Subject(options, summary, label), Body(outcome, summary, label, context), options.Recipients);
    }

    /// <summary>"2 errors, 1 warning", "all resolved", "all clear" or "test alert".</summary>
    public static string Summary(AlertOutcome outcome)
    {
        switch (outcome.Kind)
        {
            case AlertKind.Resolved:
                return "all resolved";
            case AlertKind.AllClear:
                return "all clear";
            case AlertKind.Test:
                return "test alert";
            default:
                var errors = outcome.Problems.Count(p => p.Problem.IsError);
                var warnings = outcome.Problems.Count - errors;
                return string.Join(", ", new[] { Plural(errors, "error"), Plural(warnings, "warning") }.Where(s => s.Length > 0));
        }
    }

    /// <summary>
    /// "Simon Antony (Production)". The name comes from config because the application URL is only
    /// known once configured or after the first request, so it cannot be relied on.
    /// </summary>
    public static string Label(HealthAlertOptions options, HealthAlertContext context)
    {
        var name = !string.IsNullOrWhiteSpace(options.SiteName)
            ? options.SiteName!
            : !string.IsNullOrWhiteSpace(context.Host) ? context.Host! : "Umbraco site";
        var label = SingleLine(name);

        var environment = SingleLine(context.EnvironmentName ?? string.Empty);
        return options.ShowEnvironment && environment.Length > 0 ? $"{label} ({environment})" : label;
    }

    private static string Subject(HealthAlertOptions options, string summary, string label)
    {
        var prefix = SingleLine(options.SubjectPrefix);
        var head = prefix.Length > 0 ? $"{prefix} {summary}" : summary;
        return $"{head} - {label}";
    }

    private const string Font = "font-family:Segoe UI,Helvetica,Arial,sans-serif;font-size:14px;color:#1e1e1e";
    private const string Muted = "color:#5e5a55";

    private static string Body(AlertOutcome outcome, string summary, string label, HealthAlertContext context)
    {
        var sb = new StringBuilder();
        sb.Append("<div style=\"").Append(Font).Append("\">");

        var headline = outcome.Kind switch
        {
            AlertKind.Resolved => "All resolved",
            AlertKind.AllClear => "All clear",
            AlertKind.Test => "Test alert",
            _ => summary,
        };
        sb.Append("<p style=\"margin:0 0 4px\"><strong>").Append(Encode(headline)).Append("</strong>");
        if (outcome.Passed > 0)
        {
            sb.Append(" &middot; ").Append(outcome.Passed.ToString(CultureInfo.InvariantCulture));
            sb.Append(outcome.Kind == AlertKind.Problems
                ? (outcome.Passed == 1 ? " other check" : " other checks")
                : (outcome.Passed == 1 ? " check" : " checks"));
            sb.Append(" passed");
        }

        sb.Append("</p>");
        AppendHeader(sb, label, context);

        if (outcome.Kind == AlertKind.Test)
        {
            sb.Append("<p style=\"margin:0 0 14px\">This is a test alert sent from the dashboard. If you can read it, delivery works. Nothing is wrong.</p>");
        }

        if (outcome.Problems.Count > 0)
        {
            AppendProblems(sb, outcome.Problems);
        }

        if (outcome.Resolved.Count > 0)
        {
            sb.Append("<p style=\"margin:12px 0 0\"><strong>")
              .Append(outcome.Kind == AlertKind.Resolved ? "These have cleared" : "Cleared since the last alert")
              .Append(":</strong> ").Append(Encode(string.Join(", ", outcome.Resolved))).Append("</p>");
        }

        if (outcome.BelowThreshold > 0)
        {
            sb.Append("<p style=\"margin:12px 0 0;").Append(Muted).Append("\">")
              .Append(outcome.BelowThreshold.ToString(CultureInfo.InvariantCulture))
              .Append(outcome.BelowThreshold == 1 ? " warning" : " warnings")
              .Append(" not shown, because the minimum severity is Error.</p>");
        }

        if (outcome.Snoozed.Count > 0)
        {
            sb.Append("<p style=\"margin:12px 0 0;").Append(Muted).Append("\">Snoozed: ");
            sb.Append(string.Join("; ", outcome.Snoozed.Select(s =>
            {
                var text = $"{s.Check} until {s.Until.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}";
                if (!string.IsNullOrWhiteSpace(s.Reason))
                {
                    text += $" ({SingleLine(s.Reason!)})";
                }

                return Encode(text) + (s.ExpiresSoon ? " <strong>(ends soon)</strong>" : string.Empty);
            })));
            sb.Append("</p>");
        }

        sb.Append("</div>");
        return sb.ToString();
    }

    private static void AppendProblems(StringBuilder sb, IReadOnlyList<ReportedProblem> problems)
    {
        sb.Append("<table cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;width:100%\">");
        foreach (var reported in problems)
        {
            var problem = reported.Problem;
            var colour = problem.IsError ? "#b3261e" : "#9a6200";
            sb.Append("<tr><td style=\"padding:8px 12px 8px 0;vertical-align:top;white-space:nowrap;font-weight:700;color:")
              .Append(colour).Append("\">").Append(problem.IsError ? "ERROR" : "WARNING").Append("</td>");
            sb.Append("<td style=\"padding:8px 0;border-top:1px solid #e6dfd3\">");
            sb.Append("<div style=\"font-weight:600\">").Append(Encode(problem.Check));
            if (reported.IsNew)
            {
                sb.Append(" <span style=\"font-size:11px;font-weight:700;color:#1d5fa8\">NEW</span>");
            }

            sb.Append("</div>");
            sb.Append("<div style=\"color:#3a3a3a\">").Append(Encode(problem.Message)).Append("</div>");
            if (SafeLink(problem.ReadMoreLink) is { } link)
            {
                sb.Append("<div><a href=\"").Append(Encode(link)).Append("\" style=\"color:#1d5fa8\">Read more</a></div>");
            }

            sb.Append("</td></tr>");
        }

        sb.Append("</table>");
    }

    /// <summary>Only plain http(s) links become links. Anything else, such as javascript:, is dropped.</summary>
    public static string? SafeLink(string? link) =>
        Uri.TryCreate(link?.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri.AbsoluteUri
            : null;

    private static void AppendHeader(StringBuilder sb, string label, HealthAlertContext context)
    {
        var where = string.IsNullOrWhiteSpace(context.Host) ? string.Empty : context.Host + ", ";
        sb.Append("<p style=\"margin:0 0 4px;font-size:16px\"><strong>").Append(Encode(label)).Append("</strong></p>");
        sb.Append("<p style=\"margin:0 0 14px;color:#5e5a55\">")
          .Append(Encode(where)).Append("run ")
          .Append(Encode(context.RunTime.ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture)))
          .Append("</p>");
    }

    private static string Plural(int count, string noun) =>
        count == 0 ? string.Empty : $"{count} {noun}{(count == 1 ? string.Empty : "s")}";

    /// <summary>Collapses line breaks and control characters, so config can never split a header.</summary>
    private static string SingleLine(string value) => Whitespace.Replace(value, " ").Trim();

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
