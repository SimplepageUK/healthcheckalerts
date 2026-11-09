namespace Simplepage.Umbraco.HealthCheckAlerts.Core;

/// <summary>One warning or error from a scheduled health check run.</summary>
/// <param name="Check">The health check's name, for example "Content Security Policy (CSP)".</param>
/// <param name="IsError">True for an error, false for a warning.</param>
/// <param name="ReadMoreLink">Umbraco's "read more" documentation link for the check, when it gives one. Only http(s) links are ever shown.</param>
/// <param name="Message">Plain text. Already stripped of markup by <see cref="MessageCleaner"/>.</param>
public sealed record HealthCheckProblem(string Check, bool IsError, string Message, string? ReadMoreLink = null);
