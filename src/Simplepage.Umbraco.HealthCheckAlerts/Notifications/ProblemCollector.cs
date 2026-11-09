using Simplepage.Umbraco.HealthCheckAlerts.Core;
using Umbraco.Cms.Core.HealthChecks;

namespace Simplepage.Umbraco.HealthCheckAlerts.Notifications;

/// <summary>
/// Picks the warnings and errors out of a scheduled run. Uses only the public GetResultsForStatus API,
/// because Umbraco keeps the full results dictionary internal.
/// </summary>
public static class ProblemCollector
{
    public static (IReadOnlyList<HealthCheckProblem> Problems, int PassedChecks) Collect(HealthCheckResults results)
    {
        var problems = new List<HealthCheckProblem>();
        var seenChecks = new HashSet<string>();

        // A check with any error sits in the Error bucket and can also hold warnings. A check with only
        // warnings sits in the Warning bucket. Each check is listed once, whichever bucket finds it first.
        foreach (var status in new[] { StatusResultType.Error, StatusResultType.Warning })
        {
            var bucket = results.GetResultsForStatus(status);
            if (bucket is null)
            {
                continue;
            }

            foreach (var (checkName, statuses) in bucket)
            {
                if (!seenChecks.Add(checkName))
                {
                    continue;
                }

                foreach (var s in statuses)
                {
                    if (s.ResultType is StatusResultType.Error or StatusResultType.Warning)
                    {
                        problems.Add(new HealthCheckProblem(
                            checkName,
                            s.ResultType == StatusResultType.Error,
                            MessageCleaner.Clean(s.Message),
                            string.IsNullOrWhiteSpace(s.ReadMoreLink) ? null : s.ReadMoreLink));
                    }
                }
            }
        }

        // Checks that found nothing wrong. A check that mixed a pass with a problem is counted as a problem.
        var passed = (results.GetResultsForStatus(StatusResultType.Success)?.Count ?? 0)
                     + (results.GetResultsForStatus(StatusResultType.Info)?.Count ?? 0);

        return (problems, passed);
    }
}
