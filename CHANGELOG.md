# Changelog

## 1.0.0 - 2026-11-09 (unreleased)

First release. A health check alert you can read, that says which site it is about, that stays quiet when nothing is wrong, and that you configure in the back office instead of with a deploy.

### What was wrong

Umbraco's scheduled health check email is hard to act on, for four reasons.

**It lists everything.** Every check and every result goes in, so one real problem arrives inside a screenful of "Success".

**The filters don't do what their names suggest.** `FailureOnly` only counts an *error* as a failure. A warning counts as success, so a run that finds warnings and no errors sends nothing at all, and you never find out. `Verbosity: Detailed` prints every result, and `Summary` still prints a line for every check.

**It doesn't say which site.** The subject is `Umbraco Health Check Status:` followed by the application URL. If Umbraco hasn't been told its URL and hasn't yet served a request, that is blank. The body never names the site.

**It mails from anywhere.** The scheduled job runs in any copy of the site that has notifications switched on. A developer's local copy, left running for a few days, emailed the real support address every morning.

### What it does

A new notification method, `problemsEmail`, and a back office dashboard (**Settings > Health Check Alerts**, administrators only).

- Sends only when a run has a warning or an error, and lists only those, errors first, with Umbraco's own message and its "Read more" link. Everything else is counted: *"17 other checks passed"*.
- The subject and body name the site and the environment, from a name you set rather than Umbraco's idea of its URL: `Health check: 2 errors, 2 warnings - Simon Antony (Production)`.
- Goes to one address or several, separated by commas, semicolons, spaces or new lines. Each is validated, duplicates are dropped and bad ones are reported.
- Instances running in `Development` never send by default.
- Doesn't repeat a problem that hasn't changed. New problems are marked NEW, and `ReminderEveryDays` (default 7) reminds you while one remains. `RepeatMode` `EveryRun` restores a send on every run.
- Sends a short "all resolved" email when every reported problem has cleared.
- Snooze a known problem until a date, with a reason. The email lists what is snoozed and flags a snooze that ends within three days.
- An optional webhook posts a JSON payload with an auth header (`X-Api-Key` by default). `https` only, apart from localhost, with a 10 second timeout. The key and URL are never logged or returned.
- A **Send test alert** button proves delivery works.
- Settings are saved in Umbraco's key/value table and apply from the next run, with no restart or deploy. `appsettings.json` values under `NotificationMethods:problemsEmail:Settings` are only defaults. Saved values win.
- An admin-only Management API at `/umbraco/management/api/v1/healthcheckalerts`: `settings` (GET, PUT), `status`, `snooze` (POST, DELETE) and `test`.
- Health check messages are stripped of markup and HTML-encoded before they go into the email. Subjects can't be split into extra headers.

Settings (also the `appsettings.json` defaults): `RecipientEmail`, `SiteName`, `MinimumSeverity`, `SendWhenHealthy`, `SubjectPrefix`, `IgnoreEnvironments`, `ShowEnvironment`, `WebhookUrl`, `WebhookAuthHeader`, `WebhookAuthKey`, `RepeatMode`, `ReminderEveryDays`, `SendResolved`. Snoozes are dashboard-only.

Turn Umbraco's own `email` method off (`"Enabled": false`) so you don't get two emails, but keep its `Settings.RecipientEmail`: Umbraco's constructor for that method throws without it, even when disabled.

### What has NOT changed

Umbraco's scheduler is untouched. It still decides whether the job runs, when it first runs and how often. The health checks themselves are Umbraco's, and the messages are exactly what Umbraco and your packages produce.

Mail still goes out through Umbraco's own email sender and your site's existing SMTP settings, from `Umbraco:CMS:Content:Notifications:Email`.

Running the health checks from the back office dashboard still doesn't send a notification. Only the scheduled job does.

### Testing

316 unit checks, and a smoke test (`tests/smoke/run.sh`) against a real Umbraco 17.8.1 covering Development silence, email plus webhook plus repeat suppression, persistence across a restart, and Staging with `EveryRun`. The package compiles against the Umbraco 17.0.0 floor.

Not yet verified: newer 17.x releases, installing the packed package into a separate site, and real mouse clicks on the dashboard's snooze form.
