using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Simplepage.Umbraco.HealthCheckAlerts.Core;
using Simplepage.Umbraco.HealthCheckAlerts.Notifications;
using Simplepage.Umbraco.HealthCheckAlerts.Services;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Cms.Core.HealthChecks.NotificationMethods;
using Umbraco.Cms.Core.Mail;
using Umbraco.Cms.Core.Models.Email;
using IUmbracoHostingEnvironment = Umbraco.Cms.Core.Hosting.IHostingEnvironment;

// Unit tests for the package. The pure rules in Core/ are tested directly. ProblemCollector is tested
// against real Umbraco HealthCheckResults built from fake checks. The notification method is tested with
// fake Umbraco services. The real Umbraco wiring is covered by tests/smoke/run.sh.
//
// This project references the package, which restores Umbraco.Cms.Core at 17.0.0, the oldest version we
// support. If a newer API sneaks into the package, this build breaks.

var pass = 0;
var fail = 0;

void Check(string name, bool ok, string? detail = null)
{
    if (ok) { pass++; } else { fail++; }
    Console.WriteLine($"{(ok ? "  ok  " : "  FAIL")}  {name}");
    if (!ok && detail is not null)
    {
        Console.WriteLine($"          -> {detail}");
    }
}

void Same<T>(string name, T expected, T actual) =>
    Check(name, EqualityComparer<T>.Default.Equals(expected, actual), $"expected '{expected}', got '{actual}'");

void Section(string title) => Console.WriteLine($"\n{title}\n");

HealthCheckProblem E(string check, string message = "it broke") => new(check, true, message);
HealthCheckProblem W(string check, string message = "be careful") => new(check, false, message);

var runTime = new DateTime(2026, 11, 9, 8, 59, 0);
var production = new HealthAlertContext("site.example.test", "Production", runTime);

HealthAlertOptions Opts(params (string Key, string Value)[] extra)
{
    var settings = new Dictionary<string, string> { ["RecipientEmail"] = "support@example.test" };
    foreach (var (key, value) in extra) { settings[key] = value; }
    return HealthAlertOptions.From(settings);
}

// ---------------------------------------------------------------------------------------------------
Section("RECIPIENTS");

var parsed = RecipientList.Parse("a@example.test");
Same("one address", "a@example.test", string.Join("|", parsed.Valid));

Same("comma separated", "a@example.test|b@example.test", string.Join("|", RecipientList.Parse("a@example.test,b@example.test").Valid));
Same("comma and space", "a@example.test|b@example.test", string.Join("|", RecipientList.Parse("a@example.test, b@example.test").Valid));
Same("semicolons", "a@example.test|b@example.test", string.Join("|", RecipientList.Parse("a@example.test;b@example.test").Valid));
Same("new lines and tabs", "a@example.test|b@example.test|c@example.test", string.Join("|", RecipientList.Parse("a@example.test\r\nb@example.test\tc@example.test").Valid));
Same("trailing and doubled separators", "a@example.test|b@example.test", string.Join("|", RecipientList.Parse(" ,a@example.test,,; b@example.test ,").Valid));
Same("duplicates dropped, first spelling kept", "Support@Example.test|b@example.test", string.Join("|", RecipientList.Parse("Support@Example.test, support@example.test, b@example.test").Valid));
Same("null gives none", 0, RecipientList.Parse(null).Valid.Count);
Same("empty gives none", 0, RecipientList.Parse("").Valid.Count);
Same("whitespace gives none", 0, RecipientList.Parse("  \r\n ").Valid.Count);

var mixed = RecipientList.Parse("good@example.test, not-an-email, also@example.test");
Same("one bad address does not stop the rest", "good@example.test|also@example.test", string.Join("|", mixed.Valid));
Same("the bad address is reported", "not-an-email", string.Join("|", mixed.Invalid));
Check("display-name form is not accepted", RecipientList.Parse("Bob <bob@example.test>").Valid.Count == 0);
Check("double @ refused", RecipientList.Parse("a@@example.test").Valid.Count == 0);
Check("no domain refused", RecipientList.Parse("a@").Valid.Count == 0);
Check("no local part refused", RecipientList.Parse("@example.test").Valid.Count == 0);
Check("injection attempt refused", RecipientList.Parse("a@example.test\r\nBcc:evil@example.test").Valid.Count == 1
                                  && RecipientList.Parse("a@example.test\r\nBcc:evil@example.test").Invalid.Contains("Bcc:evil@example.test"));
Check("single-label host accepted (internal mail)", RecipientList.Parse("ops@localhost").Valid.Count == 1);

// ---------------------------------------------------------------------------------------------------
Section("OPTIONS");

var defaults = HealthAlertOptions.From(new Dictionary<string, string> { ["RecipientEmail"] = "support@example.test" });
Same("default minimum severity is Warning", MinimumSeverity.Warning, defaults.MinimumSeverity);
Same("default send when healthy is off", false, defaults.SendWhenHealthy);
Same("default prefix", "Health check:", defaults.SubjectPrefix);
Same("default shows environment", true, defaults.ShowEnvironment);
Check("Development ignored by default", defaults.IgnoreEnvironments.Contains("Development") && defaults.IgnoreEnvironments.Count == 1);
Check("site name unset by default", defaults.SiteName is null);
Same("no warnings with a valid config", 0, defaults.Warnings.Count);

var nothing = HealthAlertOptions.From(null);
Same("null settings gives no recipients", 0, nothing.Recipients.Count);
Check("null settings warns about recipients", nothing.Warnings.Any(w => w.Contains("RecipientEmail")));

Same("keys are not case sensitive", MinimumSeverity.Error, HealthAlertOptions.From(new Dictionary<string, string> { ["recipientemail"] = "a@example.test", ["MINIMUMSEVERITY"] = "Error" }).MinimumSeverity);
Same("severity value not case sensitive", MinimumSeverity.Error, Opts(("MinimumSeverity", "error")).MinimumSeverity);

var badSeverity = Opts(("MinimumSeverity", "loud"));
Same("bad severity falls back to Warning", MinimumSeverity.Warning, badSeverity.MinimumSeverity);
Check("bad severity is reported", badSeverity.Warnings.Any(w => w.Contains("MinimumSeverity")));
Same("numeric severity is not accepted", MinimumSeverity.Warning, Opts(("MinimumSeverity", "7")).MinimumSeverity);

foreach (var truthy in new[] { "true", "TRUE", "yes", "1", "on", " true " })
{
    Same($"SendWhenHealthy accepts '{truthy}'", true, Opts(("SendWhenHealthy", truthy)).SendWhenHealthy);
}

foreach (var falsy in new[] { "false", "no", "0", "off" })
{
    Same($"ShowEnvironment accepts '{falsy}'", false, Opts(("ShowEnvironment", falsy)).ShowEnvironment);
}

var badBool = Opts(("SendWhenHealthy", "maybe"));
Same("bad boolean falls back to the default", false, badBool.SendWhenHealthy);
Check("bad boolean is reported", badBool.Warnings.Any(w => w.Contains("SendWhenHealthy")));

Same("empty IgnoreEnvironments means send everywhere", 0, Opts(("IgnoreEnvironments", "")).IgnoreEnvironments.Count);
var ignoreTwo = Opts(("IgnoreEnvironments", "Development, Staging"));
Check("IgnoreEnvironments lists several", ignoreTwo.IgnoreEnvironments.Contains("Development") && ignoreTwo.IgnoreEnvironments.Contains("staging"));
Same("empty SubjectPrefix is allowed", "", Opts(("SubjectPrefix", "")).SubjectPrefix);
Same("SubjectPrefix is trimmed", "Alert:", Opts(("SubjectPrefix", "  Alert:  ")).SubjectPrefix);
Same("SiteName is trimmed", "Simon Antony", Opts(("SiteName", "  Simon Antony ")).SiteName);
Check("blank SiteName counts as unset", Opts(("SiteName", "   ")).SiteName is null);
Same("invalid recipients are carried", "nope", string.Join("|", HealthAlertOptions.From(new Dictionary<string, string> { ["RecipientEmail"] = "a@example.test nope" }).InvalidRecipients));
Check("invalid recipients are reported", HealthAlertOptions.From(new Dictionary<string, string> { ["RecipientEmail"] = "a@example.test nope" }).Warnings.Any(w => w.Contains("nope")));

// ---------------------------------------------------------------------------------------------------
Section("MESSAGE CLEANING");

Same("plain text unchanged", "All good here.", MessageCleaner.Clean("All good here."));
Same("tags stripped", "The header was not found.", MessageCleaner.Clean("The header was <strong>not</strong> found."));
Same("br becomes a space", "one two", MessageCleaner.Clean("one<br/>two"));
Same("paragraphs collapse", "one two", MessageCleaner.Clean("<p>one</p><p>two</p>"));
Same("entities decoded", "Certificate & settings", MessageCleaner.Clean("Certificate &amp; settings"));
Same("encoded tag survives as text, not stripped", "a <script> b", MessageCleaner.Clean("a &lt;script&gt; b"));
Same("whitespace and new lines collapse", "a b c", MessageCleaner.Clean("  a \r\n\t b    c  "));
Same("null gives the placeholder", "(no detail given)", MessageCleaner.Clean(null));
Same("empty gives the placeholder", "(no detail given)", MessageCleaner.Clean(""));
Same("tags only gives the placeholder", "(no detail given)", MessageCleaner.Clean("<br/><p></p>"));
Check("long message truncated", MessageCleaner.Clean(new string('x', 5000)).Length <= MessageCleaner.MaxLength + 20
                              && MessageCleaner.Clean(new string('x', 5000)).EndsWith("(truncated)"));
Same("message at the limit is not truncated", MessageCleaner.MaxLength, MessageCleaner.Clean(new string('x', MessageCleaner.MaxLength)).Length);
// A "<" with no closing ">" is not a tag, so it stays as inert text. It is HTML-encoded before it reaches the email.
Same("unclosed tag does not crash and stays inert text", "keep<unclosed", MessageCleaner.Clean("keep<unclosed"));
Check("and is encoded in the email body",
    HealthAlertPlanner.Plan(new[] { new HealthCheckProblem("c", true, MessageCleaner.Clean("keep<unclosed")) }, 0,
        HealthAlertOptions.From(new Dictionary<string, string> { ["RecipientEmail"] = "a@example.test" }),
        new HealthAlertContext("h", "Production", new DateTime(2026, 11, 9, 8, 59, 0)))!.HtmlBody.Contains("keep&lt;unclosed"));

// ---------------------------------------------------------------------------------------------------
Section("PLANNER: WHETHER TO SEND");

var oneError = new[] { E("CSP") };
var oneWarning = new[] { W("Headers") };

Check("no recipients sends nothing", HealthAlertPlanner.Plan(oneError, 5, HealthAlertOptions.From(null), production) is null);
Check("healthy run sends nothing", HealthAlertPlanner.Plan([], 20, Opts(), production) is null);
Check("an error sends", HealthAlertPlanner.Plan(oneError, 20, Opts(), production) is not null);
Check("a warning alone sends (Umbraco's FailureOnly would not)", HealthAlertPlanner.Plan(oneWarning, 20, Opts(), production) is not null);
Check("Development is ignored by default", HealthAlertPlanner.Plan(oneError, 5, Opts(), production with { EnvironmentName = "Development" }) is null);
Check("environment match ignores case", HealthAlertPlanner.Plan(oneError, 5, Opts(), production with { EnvironmentName = "DEVELOPMENT" }) is null);
Check("Staging sends by default", HealthAlertPlanner.Plan(oneError, 5, Opts(), production with { EnvironmentName = "Staging" }) is not null);
Check("empty IgnoreEnvironments sends from Development", HealthAlertPlanner.Plan(oneError, 5, Opts(("IgnoreEnvironments", "")), production with { EnvironmentName = "Development" }) is not null);
Check("custom IgnoreEnvironments skips Staging", HealthAlertPlanner.Plan(oneError, 5, Opts(("IgnoreEnvironments", "Staging")), production with { EnvironmentName = "Staging" }) is null);
Check("custom IgnoreEnvironments no longer skips Development", HealthAlertPlanner.Plan(oneError, 5, Opts(("IgnoreEnvironments", "Staging")), production with { EnvironmentName = "Development" }) is not null);
Check("unknown environment name still sends", HealthAlertPlanner.Plan(oneError, 5, Opts(), production with { EnvironmentName = null }) is not null);
Check("MinimumSeverity Error ignores a warning-only run", HealthAlertPlanner.Plan(oneWarning, 5, Opts(("MinimumSeverity", "Error")), production) is null);
Check("MinimumSeverity Error still sends an error", HealthAlertPlanner.Plan(new[] { W("Headers"), E("CSP") }, 5, Opts(("MinimumSeverity", "Error")), production) is not null);

var onlyErrors = HealthAlertPlanner.Plan(new[] { W("Headers"), E("CSP") }, 5, Opts(("MinimumSeverity", "Error")), production)!;
Check("MinimumSeverity Error lists only errors", onlyErrors.HtmlBody.Contains("CSP") && !onlyErrors.HtmlBody.Contains("Headers"));
Check("MinimumSeverity Error says a warning was held back", onlyErrors.HtmlBody.Contains("1 warning not shown"));
Check("recipients are passed through", HealthAlertPlanner.Plan(oneError, 5, Opts(("RecipientEmail", "a@example.test; b@example.test")), production)!.Recipients.SequenceEqual(["a@example.test", "b@example.test"]));

var allClear = HealthAlertPlanner.Plan([], 20, Opts(("SendWhenHealthy", "true")), production);
Check("SendWhenHealthy sends an all clear", allClear is not null);
Same("all clear subject", "Health check: all clear - site.example.test (Production)", allClear!.Subject);
Check("all clear says how many passed", allClear.HtmlBody.Contains("All clear") && allClear.HtmlBody.Contains("20 checks passed"));
Check("all clear still respects the ignored environment", HealthAlertPlanner.Plan([], 20, Opts(("SendWhenHealthy", "true")), production with { EnvironmentName = "Development" }) is null);
Check("all clear after MinimumSeverity Error counts held-back warnings", HealthAlertPlanner.Plan(oneWarning, 19, Opts(("SendWhenHealthy", "true"), ("MinimumSeverity", "Error")), production)!.HtmlBody.Contains("1 warning not shown"));
Check("all clear with no recipients sends nothing", HealthAlertPlanner.Plan([], 20, HealthAlertOptions.From(new Dictionary<string, string> { ["SendWhenHealthy"] = "true" }), production) is null);

// ---------------------------------------------------------------------------------------------------
Section("PLANNER: SUBJECT AND LABEL");

Same("subject: two errors two warnings", "Health check: 2 errors, 2 warnings - site.example.test (Production)",
    HealthAlertPlanner.Plan(new[] { W("a"), E("b"), W("c"), E("d") }, 3, Opts(), production)!.Subject);
Same("subject: one error", "Health check: 1 error - site.example.test (Production)", HealthAlertPlanner.Plan(oneError, 3, Opts(), production)!.Subject);
Same("subject: one warning", "Health check: 1 warning - site.example.test (Production)", HealthAlertPlanner.Plan(oneWarning, 3, Opts(), production)!.Subject);
Same("subject: errors only", "Health check: 3 errors - site.example.test (Production)", HealthAlertPlanner.Plan(new[] { E("a"), E("b"), E("c") }, 0, Opts(), production)!.Subject);
Same("subject uses SiteName", "Health check: 1 error - Simon Antony (Production)", HealthAlertPlanner.Plan(oneError, 3, Opts(("SiteName", "Simon Antony")), production)!.Subject);
Same("subject falls back to the host", "Health check: 1 error - site.example.test (Production)", HealthAlertPlanner.Plan(oneError, 3, Opts(), production)!.Subject);
Same("subject falls back to a generic name when there is no host", "Health check: 1 error - Umbraco site (Production)", HealthAlertPlanner.Plan(oneError, 3, Opts(), production with { Host = null })!.Subject);
Same("subject can hide the environment", "Health check: 1 error - Simon Antony", HealthAlertPlanner.Plan(oneError, 3, Opts(("SiteName", "Simon Antony"), ("ShowEnvironment", "false")), production)!.Subject);
Same("subject omits empty brackets when the environment is unknown", "Health check: 1 error - Simon Antony", HealthAlertPlanner.Plan(oneError, 3, Opts(("SiteName", "Simon Antony")), production with { EnvironmentName = null })!.Subject);
Same("custom prefix", "[Alert] 1 error - Simon Antony (Production)", HealthAlertPlanner.Plan(oneError, 3, Opts(("SiteName", "Simon Antony"), ("SubjectPrefix", "[Alert]")), production)!.Subject);
Same("empty prefix", "1 error - Simon Antony (Production)", HealthAlertPlanner.Plan(oneError, 3, Opts(("SiteName", "Simon Antony"), ("SubjectPrefix", "")), production)!.Subject);

var evil = HealthAlertPlanner.Plan(oneError, 3, Opts(("SiteName", "Evil\r\nBcc: x@example.test"), ("SubjectPrefix", "Hi\nthere:")), production)!;
Check("a new line in config cannot split the subject", !evil.Subject.Contains('\r') && !evil.Subject.Contains('\n'), evil.Subject);

// ---------------------------------------------------------------------------------------------------
Section("PLANNER: BODY");

var mixedRun = HealthAlertPlanner.Plan(new[] { W("Excessive Headers", "found X-Powered-By"), E("CSP", "header missing"), W("Imaging HMAC", "no key") }, 17, Opts(("SiteName", "Simon Antony")), production)!;
var body = mixedRun.HtmlBody;
Check("body names the site and environment", body.Contains("Simon Antony (Production)"));
Check("body shows host and run time", body.Contains("site.example.test, run 9 Nov 2026 08:59"));
Check("body counts the passed checks", body.Contains("17 other checks passed"));
Check("body leads with the summary", body.Contains("1 error, 2 warnings"));
Check("errors are listed before warnings", body.IndexOf("CSP", StringComparison.Ordinal) < body.IndexOf("Excessive Headers", StringComparison.Ordinal)
                                          && body.IndexOf("CSP", StringComparison.Ordinal) < body.IndexOf("Imaging HMAC", StringComparison.Ordinal));
Check("warnings keep Umbraco's order", body.IndexOf("Excessive Headers", StringComparison.Ordinal) < body.IndexOf("Imaging HMAC", StringComparison.Ordinal));
Check("each problem shows its message", body.Contains("header missing") && body.Contains("found X-Powered-By") && body.Contains("no key"));
Check("body labels ERROR and WARNING", body.Contains(">ERROR<") && body.Contains(">WARNING<"));
Check("body has no host text when there is none", !HealthAlertPlanner.Plan(oneError, 3, Opts(), production with { Host = null })!.HtmlBody.Contains("site.example.test"));
Check("one passed check is singular", HealthAlertPlanner.Plan(oneError, 1, Opts(), production)!.HtmlBody.Contains("1 other check passed"));
Check("zero passed checks leaves the count out", !HealthAlertPlanner.Plan(oneError, 0, Opts(), production)!.HtmlBody.Contains("passed"));

Section("PLANNER: NOTHING CAN BECOME MARKUP");

var hostile = HealthAlertPlanner.Plan(
    new[] { new HealthCheckProblem("<img src=x onerror=alert(1)>", true, "<script>alert(1)</script> & \"quotes\" 'single'") },
    3, Opts(("SiteName", "<b>Site</b>"), ("SubjectPrefix", "<i>x</i>")), production with { Host = "<u>host</u>" })!;
Check("check name is encoded", !hostile.HtmlBody.Contains("<img") && hostile.HtmlBody.Contains("&lt;img"));
Check("message is encoded", !hostile.HtmlBody.Contains("<script>") && hostile.HtmlBody.Contains("&lt;script&gt;"));
Check("site name is encoded", !hostile.HtmlBody.Contains("<b>Site</b>") && hostile.HtmlBody.Contains("&lt;b&gt;Site&lt;/b&gt;"));
Check("host is encoded", !hostile.HtmlBody.Contains("<u>host</u>") && hostile.HtmlBody.Contains("&lt;u&gt;host&lt;/u&gt;"));
Check("body keeps only the tags the formatter wrote", System.Text.RegularExpressions.Regex.Matches(hostile.HtmlBody, "<(?!/?(div|p|strong|table|tr|td)\\b)[a-z]").Count == 0);

// ---------------------------------------------------------------------------------------------------
Section("COLLECTOR: REAL UMBRACO HEALTH CHECK RESULTS");

Umbraco.Cms.Core.StaticApplicationLogging.Initialize(NullLoggerFactory.Instance);

var runOfEverything = await HealthCheckResults.Create(new HealthCheck[]
{
    new PassingOne(), new PassingTwo(), new InfoOnly(), new ErrorOnly(), new WarningOnly(), new MixedCheck(), new HtmlWarning(),
});
var (found, passedCount) = ProblemCollector.Collect(runOfEverything);
Same("checks with nothing wrong are counted as passed (success and info)", 3, passedCount);
Same("every error and warning is found", 5, found.Count);
Same("errors found (Error only, and the error in Mixed check)", 2, found.Count(p => p.IsError));
Same("warnings found (Warning only, Mixed check, Html warning)", 3, found.Count(p => p.IsError == false));
Check("a mixed check appears once per problem, not once per bucket", found.Count(p => p.Check == "Mixed check") == 2);
Check("a mixed check's passing result is not listed", found.All(p => p.Message != "Fine here."));
Check("HTML in a message is cleaned at collection", found.Single(p => p.Check == "Html warning").Message == "Use <em> & stay safe");
Check("a plain check name is kept", found.Any(p => p.Check == "Error only" && p.IsError && p.Message == "No good."));

var warningsOnly = await HealthCheckResults.Create(new HealthCheck[] { new PassingOne(), new WarningOnly() });
Check("Umbraco treats a warnings-only run as successful", warningsOnly.AllChecksSuccessful);
var (warnOnlyProblems, warnOnlyPassed) = ProblemCollector.Collect(warningsOnly);
Check("the collector still finds the warning", warnOnlyProblems.Count == 1 && !warnOnlyProblems[0].IsError);
Same("and counts the passing check", 1, warnOnlyPassed);

var healthy = await HealthCheckResults.Create(new HealthCheck[] { new PassingOne(), new PassingTwo(), new InfoOnly() });
var (healthyProblems, healthyPassed) = ProblemCollector.Collect(healthy);
Same("a healthy run has no problems", 0, healthyProblems.Count);
Same("and counts every check as passed", 3, healthyPassed);

var throwing = await HealthCheckResults.Create(new HealthCheck[] { new PassingOne(), new ThrowingCheck() });
var (thrownProblems, _) = ProblemCollector.Collect(throwing);
Check("a check that throws is reported as an error", thrownProblems.Count == 1 && thrownProblems[0].IsError && thrownProblems[0].Check == "Throwing check");
Check("the exception text is in the message", thrownProblems[0].Message.Contains("kaboom"));

var nothingRun = await HealthCheckResults.Create(Array.Empty<HealthCheck>());
Same("an empty run has no problems", 0, ProblemCollector.Collect(nothingRun).Problems.Count);

// ---------------------------------------------------------------------------------------------------
Section("ENGINE: SNOOZES");

var runToday = new DateTime(2026, 11, 9, 8, 59, 0);
var nowUtc = new DateTime(2026, 11, 9, 8, 59, 0, DateTimeKind.Utc);
AlertOutcome Eval(IReadOnlyList<HealthCheckProblem> problems, HealthAlertOptions options, AlertState? state = null, HealthAlertContext? context = null, DateTime? now = null) =>
    AlertEngine.Evaluate(problems, 10, options, context ?? production, state, now ?? nowUtc);

HealthAlertOptions WithSnoozes(IEnumerable<Snooze> snoozes, params (string Key, string Value)[] extra)
{
    var settings = new Dictionary<string, string> { ["RecipientEmail"] = "support@example.test" };
    foreach (var (key, value) in extra) { settings[key] = value; }
    return HealthAlertOptions.From(settings, snoozes);
}

var snoozeCsp = new Snooze { Check = "csp", Until = "2026-12-01", Reason = "enforcing after review" };
var snoozed = Eval(new[] { E("CSP"), W("Headers") }, WithSnoozes([snoozeCsp]));
Same("a snoozed check is left out", "Headers", string.Join("|", snoozed.Problems.Select(p => p.Problem.Check)));
Same("the snooze matches ignoring case and is reported", "CSP", snoozed.Snoozed.Single().Check);
Check("the snooze reason is carried", snoozed.Snoozed.Single().Reason == "enforcing after review");
Check("a far-off snooze is not 'ends soon'", !snoozed.Snoozed.Single().ExpiresSoon);
Check("the last day of a snooze still mutes", Eval(new[] { E("CSP") }, WithSnoozes([new Snooze { Check = "CSP", Until = "2026-11-09" }])).Kind == AlertKind.None);
Check("the day after a snooze ends the problem comes back", Eval(new[] { E("CSP") }, WithSnoozes([new Snooze { Check = "CSP", Until = "2026-11-08" }])).Kind == AlertKind.Problems);
Check("a snooze ending within three days is flagged", Eval(new[] { E("CSP"), W("x") }, WithSnoozes([new Snooze { Check = "CSP", Until = "2026-11-12" }])).Snoozed.Single().ExpiresSoon);
Check("a snooze ending in four days is not", !Eval(new[] { E("CSP"), W("x") }, WithSnoozes([new Snooze { Check = "CSP", Until = "2026-11-13" }])).Snoozed.Single().ExpiresSoon);
Check("a run where everything is snoozed sends nothing", Eval(new[] { E("CSP") }, WithSnoozes([snoozeCsp])).Kind == AlertKind.None);
Check("a snooze with a bad date is ignored, not fatal", Eval(new[] { E("CSP") }, WithSnoozes([new Snooze { Check = "CSP", Until = "next week" }])).Kind == AlertKind.Problems);
Check("a snooze with no check name is ignored", Eval(new[] { E("CSP") }, WithSnoozes([new Snooze { Check = " ", Until = "2026-12-01" }])).Kind == AlertKind.Problems);
Check("a snooze for another check changes nothing", Eval(new[] { E("CSP") }, WithSnoozes([new Snooze { Check = "Other", Until = "2026-12-01" }])).Problems.Count == 1);
var snoozeMail = HealthAlertPlanner.RenderEmail(snoozed, WithSnoozes([snoozeCsp]), production)!;
Check("the email says what is snoozed and until when", snoozeMail.HtmlBody.Contains("Snoozed: CSP until 1 Dec 2026 (enforcing after review)"));
Check("the email flags a snooze that is about to end", HealthAlertPlanner.RenderEmail(
    Eval(new[] { E("CSP"), W("x") }, WithSnoozes([new Snooze { Check = "CSP", Until = "2026-11-10" }])),
    Opts(), production)!.HtmlBody.Contains("ends soon"));
Check("the snooze text is encoded", HealthAlertPlanner.RenderEmail(
    Eval(new[] { E("CSP"), W("x") }, WithSnoozes([new Snooze { Check = "CSP", Until = "2026-12-01", Reason = "<script>x</script>" }])),
    Opts(), production)!.HtmlBody.Contains("&lt;script&gt;"));
Check("a snoozed problem is not remembered as reported", !Eval(new[] { E("CSP"), W("x") }, WithSnoozes([snoozeCsp])).NewState!.Known.Any(k => k.StartsWith("CSP")));
var knownCsp = Eval(new[] { E("CSP") }, Opts()).NewState;
Check("snoozing a reported problem is not announced as cleared", Eval(new[] { E("CSP") }, WithSnoozes([snoozeCsp]), knownCsp, now: nowUtc.AddDays(1)).Resolved.Count == 0
    && Eval(new[] { E("CSP") }, WithSnoozes([snoozeCsp]), knownCsp, now: nowUtc.AddDays(1)).Kind == AlertKind.None);
Same("but every problem is kept for the dashboard", 2, Eval(new[] { E("CSP"), W("x") }, WithSnoozes([snoozeCsp])).NewState!.LastProblems.Count);

// ---------------------------------------------------------------------------------------------------
Section("ENGINE: NOT REPEATING UNCHANGED PROBLEMS");

var csp = E("CSP", "header missing");
var headers = W("Headers", "found X-Powered-By");

var first = Eval(new[] { csp }, Opts());
Check("the first run with a problem sends", first.Kind == AlertKind.Problems);
Check("the first run does not call anything new (nothing to compare with)", first.Problems.All(p => !p.IsNew));
Check("a send records when it went out", first.NewState!.LastSentUtc == nowUtc);

var second = Eval(new[] { csp }, Opts(), first.NewState, now: nowUtc.AddDays(1));
Check("the same problem the next day sends nothing", second.Kind == AlertKind.None);
Check("and the record still says when it last sent", second.NewState!.LastSentUtc == nowUtc);

var grown = Eval(new[] { csp, headers }, Opts(), first.NewState, now: nowUtc.AddDays(1));
Check("a new problem sends again", grown.Kind == AlertKind.Problems);
Same("only the new one is marked new", "Headers", string.Join("|", grown.Problems.Where(p => p.IsNew).Select(p => p.Problem.Check)));
Check("the old one is still listed", grown.Problems.Any(p => p.Problem.Check == "CSP" && !p.IsNew));
Check("the email marks the new problem", HealthAlertPlanner.RenderEmail(grown, Opts(), production)!.HtmlBody.Contains("NEW"));
Check("a first-run email has no NEW marker", !HealthAlertPlanner.RenderEmail(first, Opts(), production)!.HtmlBody.Contains("NEW"));

var changedMessage = Eval(new[] { E("CSP", "a different message") }, Opts(), first.NewState, now: nowUtc.AddDays(1));
Check("the same check with a different message counts as new", changedMessage.Kind == AlertKind.Problems && changedMessage.Problems.Single().IsNew);
var severityChange = Eval(new[] { W("CSP", "header missing") }, Opts(), first.NewState, now: nowUtc.AddDays(1));
Check("an error that becomes a warning counts as new", severityChange.Kind == AlertKind.Problems && severityChange.Problems.Single().IsNew);

Check("a reminder is sent after the interval", Eval(new[] { csp }, Opts(), first.NewState, now: nowUtc.AddDays(7)).Kind == AlertKind.Problems);
Check("not a day before it", Eval(new[] { csp }, Opts(), first.NewState, now: nowUtc.AddDays(7).AddMinutes(-1)).Kind == AlertKind.None);
Check("a custom reminder interval is honoured", Eval(new[] { csp }, Opts(("ReminderEveryDays", "2")), first.NewState, now: nowUtc.AddDays(2)).Kind == AlertKind.Problems);
Check("ReminderEveryDays 0 never reminds", Eval(new[] { csp }, Opts(("ReminderEveryDays", "0")), first.NewState, now: nowUtc.AddDays(400)).Kind == AlertKind.None);
Check("a reminder does not mark anything new", Eval(new[] { csp }, Opts(), first.NewState, now: nowUtc.AddDays(8)).Problems.All(p => !p.IsNew));
Check("a reminder restarts the interval", Eval(new[] { csp }, Opts(), first.NewState, now: nowUtc.AddDays(8)).NewState!.LastSentUtc == nowUtc.AddDays(8));
Check("EveryRun sends an unchanged problem again", Eval(new[] { csp }, Opts(("RepeatMode", "EveryRun")), first.NewState, now: nowUtc.AddDays(1)).Kind == AlertKind.Problems);

var healthyAfter = Eval([], Opts(), first.NewState, now: nowUtc.AddDays(2));
Check("everything clearing sends a resolved alert", healthyAfter.Kind == AlertKind.Resolved);
Same("it names what cleared", "CSP", string.Join("|", healthyAfter.Resolved));
Check("it clears the record so the next problem is reported", healthyAfter.NewState!.Known.Count == 0);
Check("SendResolved false stays quiet", Eval([], Opts(("SendResolved", "false")), first.NewState, now: nowUtc.AddDays(2)).Kind == AlertKind.None);
Check("a healthy run after a healthy run sends nothing", Eval([], Opts(), healthyAfter.NewState, now: nowUtc.AddDays(3)).Kind == AlertKind.None);
Check("a problem after being resolved is reported again", Eval(new[] { csp }, Opts(), healthyAfter.NewState, now: nowUtc.AddDays(3)).Kind == AlertKind.Problems);

var partlyFixed = Eval(new[] { headers }, Opts(), Eval(new[] { csp, headers }, Opts()).NewState, now: nowUtc.AddDays(1));
Check("fixing one of two problems alone sends nothing", partlyFixed.Kind == AlertKind.None);
var partlyThenNew = Eval(new[] { headers, W("Other") }, Opts(), Eval(new[] { csp, headers }, Opts()).NewState, now: nowUtc.AddDays(1));
Check("a send that follows a partial fix says what cleared", partlyThenNew.Kind == AlertKind.Problems && partlyThenNew.Resolved.SequenceEqual(["CSP"]));
Check("and the email says so", HealthAlertPlanner.RenderEmail(partlyThenNew, Opts(), production)!.HtmlBody.Contains("Cleared since the last alert:</strong> CSP"));
Check("a resolved email is not sent when SendWhenHealthy would send an all clear", Eval([], Opts(("SendWhenHealthy", "true")), first.NewState, now: nowUtc.AddDays(2)).Kind == AlertKind.Resolved);
Check("SendWhenHealthy still sends an all clear when nothing was resolved", Eval([], Opts(("SendWhenHealthy", "true")), null).Kind == AlertKind.AllClear);

var resolvedMail = HealthAlertPlanner.RenderEmail(healthyAfter, Opts(("SiteName", "Simon Antony")), production)!;
Same("resolved subject", "Health check: all resolved - Simon Antony (Production)", resolvedMail.Subject);
Check("resolved body names what cleared", resolvedMail.HtmlBody.Contains("All resolved") && resolvedMail.HtmlBody.Contains("These have cleared:</strong> CSP"));

var ignoredRun = Eval(new[] { csp }, Opts(), first.NewState, production with { EnvironmentName = "Development" });
Check("an ignored environment sends nothing", ignoredRun.Kind == AlertKind.None);
Check("and leaves the stored state alone", ignoredRun.NewState is null);
Check("a severity-filtered run holds warnings back from the record", Eval(new[] { headers }, Opts(("MinimumSeverity", "Error"))).NewState!.Known.Count == 0);

// ---------------------------------------------------------------------------------------------------
Section("ENGINE: TEST ALERT");

var testOutcome = AlertEngine.Test();
var testMail = HealthAlertPlanner.RenderEmail(testOutcome, Opts(("SiteName", "Simon Antony")), production with { EnvironmentName = "Development" })!;
Check("a test alert renders even where the environment is ignored", testMail is not null);
Same("test subject", "Health check: test alert - Simon Antony (Development)", testMail!.Subject);
Check("test body says nothing is wrong", testMail.HtmlBody.Contains("Nothing is wrong"));
Check("test body shows a read more link", testMail.HtmlBody.Contains("href=\"https://docs.umbraco.com/\""));
Check("a test alert leaves the stored state alone", testOutcome.NewState is null);

// ---------------------------------------------------------------------------------------------------
Section("READ MORE LINKS");

string LinkBody(string? link) => HealthAlertPlanner.Plan(new[] { new HealthCheckProblem("c", true, "m", link) }, 0, Opts(), production)!.HtmlBody;
Check("an https link becomes a Read more link", LinkBody("https://docs.umbraco.com/x?a=1&b=2").Contains("<a href=\"https://docs.umbraco.com/x?a=1&amp;b=2\""));
Check("an http link is allowed", LinkBody("http://example.test/").Contains("Read more"));
Check("a javascript link is dropped", !LinkBody("javascript:alert(1)").Contains("href"));
Check("a data link is dropped", !LinkBody("data:text/html,x").Contains("href"));
Check("a relative link is dropped", !LinkBody("/docs").Contains("href"));
Check("no link, no Read more", !LinkBody(null).Contains("Read more"));
Check("a quote in a link cannot break out of the attribute", !LinkBody("https://example.test/\" onclick=\"x").Contains("onclick=\"x"));

// ---------------------------------------------------------------------------------------------------
Section("SETTINGS: SAVED OVER DEFAULTS");

var appDefaults = new Dictionary<string, string> { ["RecipientEmail"] = "default@example.test", ["SiteName"] = "Default name", ["SubjectPrefix"] = "Default:" };
Same("no saved settings means the appDefaults", "default@example.test", string.Join("|", AlertSettings.Resolve(appDefaults, null).Recipients));
Same("a saved field wins", "saved@example.test", string.Join("|", AlertSettings.Resolve(appDefaults, new AlertSettings { RecipientEmail = "saved@example.test" }).Recipients));
Same("an unsaved field keeps the default", "Default name", AlertSettings.Resolve(appDefaults, new AlertSettings { RecipientEmail = "saved@example.test" }).SiteName);
Same("a saved empty string is a real choice", "", AlertSettings.Resolve(appDefaults, new AlertSettings { SubjectPrefix = "" }).SubjectPrefix);
Check("appDefaults can be absent altogether", AlertSettings.Resolve(null, new AlertSettings { RecipientEmail = "a@example.test" }).Recipients.Count == 1);
Check("saved snoozes flow through", AlertSettings.Resolve(appDefaults, new AlertSettings { Snoozes = [snoozeCsp] }).Snoozes.Count == 1);

var roundTrip = new AlertSettings
{
    RecipientEmail = "a@example.test, b@example.test", SiteName = "S", MinimumSeverity = "Error", SendWhenHealthy = true, SubjectPrefix = "", IgnoreEnvironments = "Staging",
    ShowEnvironment = false, WebhookUrl = "https://hooks.example.test/x", WebhookAuthHeader = "X-Key", WebhookAuthKey = "k", RepeatMode = "EveryRun", ReminderEveryDays = 3,
    SendResolved = false, Snoozes = [snoozeCsp],
};
var back = AlertSettings.FromJson(roundTrip.ToJson())!;
Check("settings survive being stored as JSON", back.RecipientEmail == roundTrip.RecipientEmail && back.SendWhenHealthy == true && back.SendResolved == false
                                              && back.ReminderEveryDays == 3 && back.SubjectPrefix == "" && back.Snoozes.Single().Check == "csp");
Check("a field that was never set stays unset", AlertSettings.FromJson(new AlertSettings { SiteName = "x" }.ToJson())!.SendWhenHealthy is null);
Check("damaged JSON gives null, not an exception", AlertSettings.FromJson("{not json") is null);
Check("empty text gives null", AlertSettings.FromJson("") is null && AlertSettings.FromJson(null) is null);
Check("a document with null snoozes still loads", AlertSettings.FromJson("{\"snoozes\":null}")!.Snoozes.Count == 0);
var stateBack = AlertState.FromJson(new AlertState { Known = ["a"], LastProblems = [E("CSP", "m")], LastSentUtc = nowUtc }.ToJson())!;
Check("state survives being stored as JSON", stateBack.Known.Single() == "a" && stateBack.LastProblems.Single().Check == "CSP" && stateBack.LastSentUtc == nowUtc);
Check("damaged state gives null", AlertState.FromJson("[]x") is null);

// ---------------------------------------------------------------------------------------------------
Section("OPTIONS: WEBHOOK AND REPEAT");

var hookOpts = Opts(("WebhookUrl", "https://hooks.example.test/abc"), ("WebhookAuthHeader", "Authorization"), ("WebhookAuthKey", "Bearer s3cret"));
Same("webhook URL is kept", "https://hooks.example.test/abc", hookOpts.WebhookUrl);
Same("auth header is kept", "Authorization", hookOpts.WebhookAuthHeader);
Same("default auth header", "X-Api-Key", Opts(("WebhookUrl", "https://h.example.test/")).WebhookAuthHeader);
Check("http to a public host is refused", Opts(("WebhookUrl", "http://hooks.example.test/")).WebhookUrl is null && Opts(("WebhookUrl", "http://hooks.example.test/")).Warnings.Any(w => w.Contains("WebhookUrl")));
Check("http to localhost is allowed for testing", Opts(("WebhookUrl", "http://127.0.0.1:9999/x")).WebhookUrl is not null);
Check("a URL with embedded credentials is refused", Opts(("WebhookUrl", "https://user:pass@hooks.example.test/")).WebhookUrl is null);
Check("a non-URL is refused", Opts(("WebhookUrl", "not a url")).WebhookUrl is null);
Check("file and ftp URLs are refused", Opts(("WebhookUrl", "file:///etc/passwd")).WebhookUrl is null && Opts(("WebhookUrl", "ftp://x.example.test/")).WebhookUrl is null);
Check("a bad header name is replaced", Opts(("WebhookAuthHeader", "Bad Header: x")).WebhookAuthHeader == "X-Api-Key");
Check("a key with a line break is ignored", Opts(("WebhookAuthKey", "a\r\nX-Evil: 1")).WebhookAuthKey is null);
Check("no webhook by default", Opts().WebhookUrl is null);
Same("repeat defaults to OnChange", RepeatMode.OnChange, Opts().RepeatMode);
Same("EveryRun is read", RepeatMode.EveryRun, Opts(("RepeatMode", "everyrun")).RepeatMode);
Check("a bad repeat mode falls back with a warning", Opts(("RepeatMode", "sometimes")).RepeatMode == RepeatMode.OnChange && Opts(("RepeatMode", "sometimes")).Warnings.Any(w => w.Contains("RepeatMode")));
Same("reminder defaults to 7 days", 7, Opts().ReminderEveryDays);
Same("reminder is read", 14, Opts(("ReminderEveryDays", "14")).ReminderEveryDays);
Check("a bad reminder falls back with a warning", Opts(("ReminderEveryDays", "soon")).ReminderEveryDays == 7 && Opts(("ReminderEveryDays", "-3")).ReminderEveryDays == 7 && Opts(("ReminderEveryDays", "9999")).ReminderEveryDays == 7);
Check("SendResolved defaults on", Opts().SendResolved);

// ---------------------------------------------------------------------------------------------------
Section("WEBHOOK PAYLOAD");

var payloadOutcome = Eval(new[] { E("CSP", "header missing") with { ReadMoreLink = "https://docs.umbraco.com/csp" }, W("Headers", "found X-Powered-By") }, hookOpts, Eval([], hookOpts).NewState);
using var doc = System.Text.Json.JsonDocument.Parse(WebhookPayload.Build(payloadOutcome, hookOpts, production));
var root = doc.RootElement;
Same("payload status", "problems", root.GetProperty("status").GetString());
Same("payload site label", "site.example.test (Production)", root.GetProperty("site").GetString());
Same("payload environment", "Production", root.GetProperty("environment").GetString());
Same("payload counts errors", 1, root.GetProperty("errors").GetInt32());
Same("payload counts warnings", 1, root.GetProperty("warnings").GetInt32());
Same("payload passed checks", 10, root.GetProperty("passedChecks").GetInt32());
Same("payload lists both problems", 2, root.GetProperty("problems").GetArrayLength());
var firstProblem = root.GetProperty("problems")[0];
Same("errors come first", "CSP", firstProblem.GetProperty("check").GetString());
Same("severity is text", "error", firstProblem.GetProperty("severity").GetString());
Same("read more url is included", "https://docs.umbraco.com/csp", firstProblem.GetProperty("readMoreUrl").GetString());
Same("new is flagged", true, firstProblem.GetProperty("isNew").GetBoolean());
Check("the auth key is never in the payload", !WebhookPayload.Build(payloadOutcome, hookOpts, production).Contains("s3cret"));
Check("the webhook URL is never in the payload", !WebhookPayload.Build(payloadOutcome, hookOpts, production).Contains("hooks.example.test"));
Check("an unsafe read more link is dropped from the payload", System.Text.Json.JsonDocument.Parse(WebhookPayload.Build(
    Eval(new[] { E("c", "m") with { ReadMoreLink = "javascript:x" } }, hookOpts), hookOpts, production)).RootElement.GetProperty("problems")[0].GetProperty("readMoreUrl").ValueKind == System.Text.Json.JsonValueKind.Null);
Same("resolved status", "resolved", System.Text.Json.JsonDocument.Parse(WebhookPayload.Build(healthyAfter, hookOpts, production)).RootElement.GetProperty("status").GetString());
Same("test status", "test", System.Text.Json.JsonDocument.Parse(WebhookPayload.Build(AlertEngine.Test(), hookOpts, production)).RootElement.GetProperty("status").GetString());

// ---------------------------------------------------------------------------------------------------
Section("NOTIFICATION METHOD: WIRING WITH FAKE UMBRACO SERVICES");

(ProblemsEmailNotificationMethod Method, FakeEmailSender Sender, FakeAlertStore Store, FakeWebhookSender Hook) Build(
    Dictionary<string, string>? settings,
    string environment = "Production",
    string? host = "site.example.test",
    bool enabled = true,
    bool configured = true,
    FakeAlertStore? store = null)
{
    var methods = new Dictionary<string, HealthChecksNotificationMethodSettings>();
    if (configured)
    {
        methods[ProblemsEmailNotificationMethod.Alias] = new HealthChecksNotificationMethodSettings
        {
            Enabled = enabled,
            Settings = settings ?? new Dictionary<string, string>(),
        };
    }

    var healthChecks = new StaticOptionsMonitor<HealthChecksSettings>(
        new HealthChecksSettings { Notification = new HealthChecksNotificationSettings { NotificationMethods = methods } });
    var content = new StaticOptionsMonitor<ContentSettings>(
        new ContentSettings { Notifications = new ContentNotificationSettings { Email = "alerts@example.test" } });
    var sender = new FakeEmailSender();
    var hook = new FakeWebhookSender();
    store ??= new FakeAlertStore();
    var runtime = new AlertRuntime(store, healthChecks, new FakeHostingEnvironment(host), new FakeWebHostEnvironment(environment));
    var dispatcher = new AlertDispatcher(sender, content, hook, NullLogger<AlertDispatcher>.Instance);
    var method = new ProblemsEmailNotificationMethod(healthChecks, runtime, dispatcher, NullLogger<ProblemsEmailNotificationMethod>.Instance);
    return (method, sender, store, hook);
}

Dictionary<string, string> Config(params (string Key, string Value)[] extra)
{
    var settings = new Dictionary<string, string> { ["RecipientEmail"] = "one@example.test, two@example.test", ["SiteName"] = "Simon Antony" };
    foreach (var (key, value) in extra) { settings[key] = value; }
    return settings;
}

var problemRun = await HealthCheckResults.Create(new HealthCheck[] { new PassingOne(), new ErrorOnly(), new WarningOnly() });
var cleanRun = await HealthCheckResults.Create(new HealthCheck[] { new PassingOne(), new PassingTwo() });
var errorOnlyRun = await HealthCheckResults.Create(new HealthCheck[] { new PassingOne(), new ErrorOnly() });

Same("alias is the documented config key", "problemsEmail",
    typeof(ProblemsEmailNotificationMethod).GetCustomAttributes(typeof(HealthCheckNotificationMethodAttribute), false)
        .Cast<HealthCheckNotificationMethodAttribute>().Single().Alias);
Check("it is a health check notification method Umbraco will discover", typeof(IHealthCheckNotificationMethod).IsAssignableFrom(typeof(ProblemsEmailNotificationMethod)));

var (live, liveSender, liveStore, _) = Build(Config());
Check("enabled when configured", live.Enabled);
await live.SendAsync(problemRun);
Same("a problem run sends exactly one email", 1, liveSender.Sent.Count);
var sent = liveSender.Sent.Single();
Same("one message goes to every recipient", "one@example.test|two@example.test", string.Join("|", sent.To));
Same("from address is the Umbraco notifications email", "alerts@example.test", sent.From);
Same("the message is HTML", true, sent.IsBodyHtml);
Same("subject names the site and environment", "Health check: 1 error, 1 warning - Simon Antony (Production)", sent.Subject);
Check("body lists the problems", sent.Body!.Contains("Error only") && sent.Body.Contains("Warning only"));
Check("body does not list a passing check", !sent.Body.Contains("Passing one"));
Same("email type is the health check type", Umbraco.Cms.Core.Constants.Web.EmailTypes.HealthCheck, liveSender.LastEmailType);
Same("the run is remembered", 2, liveStore.State!.Known.Count);
Same("every problem is kept for the dashboard", 2, liveStore.State.LastProblems.Count);

await live.SendAsync(problemRun);
Same("the same problems the next run send nothing more", 1, liveSender.Sent.Count);

var (quiet, quietSender, _, _) = Build(Config());
await quiet.SendAsync(cleanRun);
Same("a healthy run sends nothing", 0, quietSender.Sent.Count);

var (clear, clearSender, _, _) = Build(Config(("SendWhenHealthy", "true")));
await clear.SendAsync(cleanRun);
Same("a healthy run sends an all clear when asked", 1, clearSender.Sent.Count);
Check("all clear subject", clearSender.Sent[0].Subject == "Health check: all clear - Simon Antony (Production)");

var (cycle, cycleSender, _, _) = Build(Config());
await cycle.SendAsync(errorOnlyRun);
await cycle.SendAsync(problemRun);
Same("a new problem sends a second email", 2, cycleSender.Sent.Count);
Check("which marks it new", cycleSender.Sent[1].Body!.Contains("NEW"));
await cycle.SendAsync(cleanRun);
Same("everything clearing sends a third", 3, cycleSender.Sent.Count);
Check("which is the resolved email", cycleSender.Sent[2].Subject == "Health check: all resolved - Simon Antony (Production)");
await cycle.SendAsync(cleanRun);
Same("and then it goes quiet", 3, cycleSender.Sent.Count);

var (dev, devSender, devStore, _) = Build(Config(), environment: "Development");
Check("still enabled in Development, it just does not send", dev.Enabled);
await dev.SendAsync(problemRun);
Same("Development sends nothing by default", 0, devSender.Sent.Count);
Check("and records nothing", devStore.State is null);

var (devOn, devOnSender, _, _) = Build(Config(("IgnoreEnvironments", "")), environment: "Development");
await devOn.SendAsync(problemRun);
Same("Development sends when ignoring is switched off", 1, devOnSender.Sent.Count);

var (noHost, noHostSender, _, _) = Build(Config(("SiteName", "")), host: null);
await noHost.SendAsync(problemRun);
Same("no application URL and no SiteName still gives a label", "Health check: 1 error, 1 warning - Umbraco site (Production)", noHostSender.Sent.Single().Subject);

var (noHostNamed, noHostNamedSender, _, _) = Build(Config(), host: null);
await noHostNamed.SendAsync(problemRun);
Same("no application URL with a SiteName names the site", "Health check: 1 error, 1 warning - Simon Antony (Production)", noHostNamedSender.Sent.Single().Subject);

var (onlyBad, onlyBadSender, _, _) = Build(new Dictionary<string, string> { ["RecipientEmail"] = "nope, also-nope" });
await onlyBad.SendAsync(problemRun);
Same("no valid recipient sends nothing and does not throw", 0, onlyBadSender.Sent.Count);

var (missing, missingSender, missingStore, _) = Build(new Dictionary<string, string>());
await missing.SendAsync(problemRun);
Same("no recipient at all sends nothing", 0, missingSender.Sent.Count);
Check("and does not mark the problem as reported", missingStore.State!.Known.Count == 0 && missingStore.State.LastSentUtc is null);

var (partlyBad, partlyBadSender, _, _) = Build(Config(("RecipientEmail", "good@example.test, nonsense")));
await partlyBad.SendAsync(problemRun);
Same("one bad address leaves the good address its email", "good@example.test", string.Join("|", partlyBadSender.Sent.Single().To));

var (off, offSender, offStore, _) = Build(Config(), enabled: false);
Check("Enabled false in config switches it off", !off.Enabled);
await off.SendAsync(problemRun);
Same("and nothing is sent when disabled", 0, offSender.Sent.Count);
Check("and nothing is stored", offStore.State is null);

var (absent, absentSender, _, _) = Build(null, configured: false);
Check("no problemsEmail section means off", !absent.Enabled);
await absent.SendAsync(problemRun);
Same("and nothing is sent without a section", 0, absentSender.Sent.Count);

var (garbage, garbageSender, _, _) = Build(Config(("MinimumSeverity", "loud"), ("SendWhenHealthy", "maybe"), ("ShowEnvironment", "x")));
await garbage.SendAsync(problemRun);
Same("bad option values fall back to the defaults", 1, garbageSender.Sent.Count);

var (errorsOnly, errorsOnlySender, _, _) = Build(Config(("MinimumSeverity", "Error")));
await errorsOnly.SendAsync(problemRun);
Check("MinimumSeverity Error keeps the error and holds back the warning", errorsOnlySender.Sent.Single().Body!.Contains("Error only")
                                                                            && !errorsOnlySender.Sent.Single().Body!.Contains("Warning only")
                                                                            && errorsOnlySender.Sent.Single().Body!.Contains("1 warning not shown"));

// Settings saved in the dashboard beat appsettings.json, and apply to the next run without a restart.
var savedStore = new FakeAlertStore { Settings = new AlertSettings { RecipientEmail = "dash@example.test", SiteName = "Dashboard name" } };
var (saved, savedSender, _, _) = Build(Config(), store: savedStore);
await saved.SendAsync(errorOnlyRun);
Same("saved recipients win over appsettings.json", "dash@example.test", string.Join("|", savedSender.Sent.Single().To));
Check("saved site name wins", savedSender.Sent.Single().Subject!.EndsWith("Dashboard name (Production)"));
savedStore.Settings = new AlertSettings { RecipientEmail = "later@example.test" };
await saved.SendAsync(problemRun);
Same("a change saved between runs applies to the next run", "later@example.test", string.Join("|", savedSender.Sent.Last().To));

var snoozeStore = new FakeAlertStore { Settings = new AlertSettings { Snoozes = [new Snooze { Check = "Error only", Until = "2999-01-01" }] } };
var (snoozedMethod, snoozedSender, _, _) = Build(Config(), store: snoozeStore);
await snoozedMethod.SendAsync(problemRun);
Check("a snoozed check is not in the email", !snoozedSender.Sent.Single().Body!.Contains(">Error only") && snoozedSender.Sent.Single().Body!.Contains(">Warning only"));
Check("and the email says it is snoozed", snoozedSender.Sent.Single().Body!.Contains("Snoozed: Error only until 1 Jan 2999"));

// Webhook.
var hookConfig = Config(("WebhookUrl", "https://hooks.example.test/abc"), ("WebhookAuthKey", "s3cret"));
var (hooked, hookedSender, _, hookSender) = Build(hookConfig);
await hooked.SendAsync(problemRun);
Same("the webhook is called as well as the email", 1, hookSender.Calls.Count);
Same("the email still goes", 1, hookedSender.Sent.Count);
Check("the webhook gets the key through its options", hookSender.Calls[0].Options.WebhookAuthKey == "s3cret");
Check("the webhook body is the JSON payload", hookSender.Calls[0].Json.Contains("\"status\":\"problems\""));

var (hookOnly, hookOnlySender, hookOnlyStore, hookOnlyHook) = Build(Config(("RecipientEmail", ""), ("WebhookUrl", "https://hooks.example.test/abc")));
await hookOnly.SendAsync(problemRun);
Check("a webhook alone is enough", hookOnlySender.Sent.Count == 0 && hookOnlyHook.Calls.Count == 1 && hookOnlyStore.State!.Known.Count == 2);

var (hookDown, hookDownSender, hookDownStore, hookDownHook) = Build(hookConfig);
hookDownHook.Fail = true;
await hookDown.SendAsync(problemRun);
Check("a failing webhook does not stop the email", hookDownSender.Sent.Count == 1);
Check("and the run is still remembered, because the email got through", hookDownStore.State!.Known.Count == 2);

var (mailDown, mailDownSender, mailDownStore, mailDownHook) = Build(hookConfig);
mailDownSender.Fail = true;
await mailDown.SendAsync(problemRun);
Check("a failing email does not stop the webhook", mailDownHook.Calls.Count == 1);

var (allDown, allDownSender, allDownStore, allDownHook) = Build(Config());
allDownSender.Fail = true;
await allDown.SendAsync(problemRun);
Check("when nothing gets through the problem is not recorded as reported", allDownStore.State!.Known.Count == 0 && allDownStore.State.LastSentUtc is null);
Check("but what the run found is kept for the dashboard", allDownStore.State.LastProblems.Count == 2);
allDownSender.Fail = false;
await allDown.SendAsync(problemRun);
Same("so the next run tries again", 1, allDownSender.Sent.Count);

var (brokenStore, brokenSender, _, _) = Build(Config(), store: new FakeAlertStore { ThrowOnSave = true });
await brokenStore.SendAsync(problemRun);
Same("a store that cannot save does not stop the email", 1, brokenSender.Sent.Count);

// Dispatcher on its own: the dashboard's test button uses it.
var (_, testSender, _, testHook) = Build(hookConfig);
var runtimeForTest = new AlertRuntime(new FakeAlertStore(), new StaticOptionsMonitor<HealthChecksSettings>(new HealthChecksSettings
{
    Notification = new HealthChecksNotificationSettings { NotificationMethods = new Dictionary<string, HealthChecksNotificationMethodSettings> { ["problemsEmail"] = new() { Enabled = true, Settings = hookConfig } } },
}), new FakeHostingEnvironment("site.example.test"), new FakeWebHostEnvironment("Development"));
var testDispatcher = new AlertDispatcher(testSender, new StaticOptionsMonitor<ContentSettings>(new ContentSettings { Notifications = new ContentNotificationSettings { Email = "a@example.test" } }), testHook, NullLogger<AlertDispatcher>.Instance);
var testReport = await testDispatcher.DeliverAsync(AlertEngine.Test(), runtimeForTest.CurrentOptions(), runtimeForTest.CreateContext());
Check("a test alert goes out by email even from Development", testSender.Sent.Count == 1 && testSender.Sent[0].Subject!.Contains("test alert") && testReport.Delivered);
Check("and by webhook", testHook.Calls.Count == 1 && testHook.Calls[0].Json.Contains("\"status\":\"test\""));
Check("the report says what happened without leaking the URL or key", testReport.Webhook is not null && !testReport.Webhook.Message.Contains("abc") && !testReport.Webhook.Message.Contains("s3cret"));
var nothingSet = await testDispatcher.DeliverAsync(AlertEngine.Test(), HealthAlertOptions.From(null), runtimeForTest.CreateContext());
Check("with no channel set the report says so", !nothingSet.Configured && !nothingSet.Delivered);

// ---------------------------------------------------------------------------------------------------
Section("BOUNDARY: CORE STAYS FREE OF UMBRACO AND ASP.NET CORE");

var coreFolder = FindCoreFolder();
var coreFiles = Directory.GetFiles(coreFolder, "*.cs", SearchOption.AllDirectories);
Check("found the Core folder", coreFiles.Length >= 8, coreFolder);
foreach (var file in coreFiles)
{
    var text = File.ReadAllText(file);
    var offending = new[] { "using Umbraco", "using Microsoft.AspNetCore", "using Microsoft.Extensions", "Umbraco.Cms" }.Where(text.Contains).ToList();
    Check($"{Path.GetFileName(file)} has no Umbraco or ASP.NET Core reference", offending.Count == 0, string.Join(", ", offending));
}

Console.WriteLine($"\n{pass} passed, {fail} failed\n");
return fail == 0 ? 0 : 1;

static string FindCoreFolder()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        var candidate = Path.Combine(dir.FullName, "src", "Simplepage.Umbraco.HealthCheckAlerts", "Core");
        if (Directory.Exists(candidate)) { return candidate; }
        dir = dir.Parent;
    }

    return "src/Simplepage.Umbraco.HealthCheckAlerts/Core";
}

// ---------------------------------------------------------------------------------------------------
// Fakes. Real Umbraco health check classes are used for the results; only the services around the
// notification method are faked.

sealed class FakeEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = [];
    public string? LastEmailType { get; private set; }
    public bool Fail { get; set; }

    public Task SendAsync(EmailMessage message, string emailType) => SendAsync(message, emailType, false);

    public Task SendAsync(EmailMessage message, string emailType, bool enableNotification)
    {
        if (Fail)
        {
            throw new InvalidOperationException("smtp is down");
        }

        Sent.Add(message);
        LastEmailType = emailType;
        return Task.CompletedTask;
    }

    public bool CanSendRequiredEmail() => true;
}

sealed class FakeAlertStore : IAlertStore
{
    public AlertSettings? Settings { get; set; }
    public AlertState? State { get; set; }
    public bool ThrowOnSave { get; set; }

    // Round-trip through JSON like the real store, so a field that does not serialise fails a test.
    public AlertSettings? LoadSettings() => AlertSettings.FromJson(Settings?.ToJson());
    public void SaveSettings(AlertSettings settings) => Settings = settings;
    public AlertState? LoadState() => AlertState.FromJson(State?.ToJson());

    public void SaveState(AlertState state)
    {
        if (ThrowOnSave)
        {
            throw new InvalidOperationException("database is down");
        }

        State = state;
    }
}

sealed class FakeWebhookSender : IWebhookSender
{
    public List<(HealthAlertOptions Options, string Json)> Calls { get; } = [];
    public bool Fail { get; set; }

    public Task<DeliveryResult> SendAsync(HealthAlertOptions options, string json, CancellationToken cancellationToken = default)
    {
        Calls.Add((options, json));
        return Task.FromResult(Fail ? new DeliveryResult(false, "Webhook rejected by hooks.example.test (500).") : new DeliveryResult(true, "Webhook accepted by hooks.example.test (200)."));
    }
}

sealed class FakeHostingEnvironment(string? host) : IUmbracoHostingEnvironment
{
    // Umbraco returns null here when no application URL is configured and detection is off,
    // even though the property is declared non-nullable.
    public Uri ApplicationMainUrl { get; } = host is null ? null! : new Uri($"https://{host}/");
    public string? SiteName => "test";
    public string ApplicationId => "test";
    public string ApplicationPhysicalPath => "/";
    public string LocalTempPath => Path.GetTempPath();
    public string ApplicationVirtualPath => "/";
    public bool IsDebugMode => false;
    public bool IsHosted => true;
    public string MapPathWebRoot(string path) => path;
    public string MapPathContentRoot(string path) => path;
    public string ToAbsolute(string virtualPath) => virtualPath;
    public void EnsureApplicationMainUrl(Uri? currentApplicationUrl) { }
}

sealed class FakeWebHostEnvironment(string environmentName) : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;
    public string ApplicationName { get; set; } = "Tests";
    public string WebRootPath { get; set; } = "/";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; } = "/";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

abstract class FakeCheck(params (string Message, StatusResultType Type)[] statuses) : HealthCheck
{
    public override Task<IEnumerable<HealthCheckStatus>> GetStatusAsync() =>
        Task.FromResult<IEnumerable<HealthCheckStatus>>(statuses.Select(s => new HealthCheckStatus(s.Message) { ResultType = s.Type }).ToList());

    public override HealthCheckStatus ExecuteAction(HealthCheckAction action) => throw new NotImplementedException();
}

[HealthCheck("22222222-2222-2222-2222-000000000001", "Passing one")]
sealed class PassingOne() : FakeCheck(("All fine.", StatusResultType.Success));

[HealthCheck("22222222-2222-2222-2222-000000000002", "Passing two")]
sealed class PassingTwo() : FakeCheck(("Fine.", StatusResultType.Success), ("Also fine.", StatusResultType.Success));

[HealthCheck("22222222-2222-2222-2222-000000000003", "Info only")]
sealed class InfoOnly() : FakeCheck(("For your information.", StatusResultType.Info));

[HealthCheck("22222222-2222-2222-2222-000000000004", "Error only")]
sealed class ErrorOnly() : FakeCheck(("No good.", StatusResultType.Error));

[HealthCheck("22222222-2222-2222-2222-000000000005", "Warning only")]
sealed class WarningOnly() : FakeCheck(("Be careful.", StatusResultType.Warning));

[HealthCheck("22222222-2222-2222-2222-000000000006", "Mixed check")]
sealed class MixedCheck() : FakeCheck(("Fine here.", StatusResultType.Success), ("Weak settings.", StatusResultType.Warning), ("Cookie not secure.", StatusResultType.Error));

[HealthCheck("22222222-2222-2222-2222-000000000007", "Html warning")]
sealed class HtmlWarning() : FakeCheck(("Use &lt;em&gt; &amp; <strong>stay</strong> safe", StatusResultType.Warning));

[HealthCheck("22222222-2222-2222-2222-000000000008", "Throwing check")]
sealed class ThrowingCheck : HealthCheck
{
    public override Task<IEnumerable<HealthCheckStatus>> GetStatusAsync() => throw new InvalidOperationException("kaboom");
    public override HealthCheckStatus ExecuteAction(HealthCheckAction action) => throw new NotImplementedException();
}
