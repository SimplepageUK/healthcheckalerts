# Health Check Alerts for Umbraco

Tells you when a scheduled health check finds a problem, and says nothing when it doesn't. Settings are edited in the back office and apply from the next run, with no deploy.

`Simplepage.Umbraco.HealthCheckAlerts` is free and MIT licensed, published by Simplepage Ltd. It's on [NuGet](https://www.nuget.org/packages/Simplepage.Umbraco.HealthCheckAlerts) and the [Umbraco Marketplace](https://marketplace.umbraco.com/package/simplepage.umbraco.healthcheckalerts).

```
dotnet add package Simplepage.Umbraco.HealthCheckAlerts
```

Then [switch it on](#quick-start) and set the recipients in **Settings > Health Check Alerts**.

## The problem

Umbraco can run its health checks on a schedule and email you the results. Out of the box, that email has four problems.

- **It lists everything.** Every check and every result, so one real problem arrives inside a screenful of "Success".
- **The filters don't filter the way their names suggest.** `FailureOnly` only counts an *error* as a failure. A warning counts as success, so a run with warnings and no errors sends nothing at all. `Verbosity: Detailed` prints every result, and `Summary` still prints one line for every check.
- **It doesn't say which site.** The subject is `Umbraco Health Check Status:` followed by the application URL. That URL is blank if Umbraco hasn't been told it (`WebRouting:UmbracoApplicationUrl`) and hasn't yet served a request, and the body never names the site. If you look after several sites and send them all to one support address, you can't tell which one is complaining.
- **It mails from anywhere.** The scheduled job runs in any copy of the site that has notifications switched on, including a developer's local copy that someone forgot to stop. That copy uses the real mail settings and emails the real recipients.

And changing who gets the email means editing `appsettings.json` and deploying.

## What it does

A notification method called `problemsEmail`, plus a dashboard to configure it.

- **It sends only when something is wrong.** No warnings and no errors, no email. (`SendWhenHealthy` adds a short all-clear if you would rather hear that everything is fine.)
- **It lists only the problems, errors first.** Each one has the check name, Umbraco's own message and, where Umbraco gives one, a **Read more** link. Everything else is counted: *"17 other checks passed"*.
- **It says which site and which environment.** From a name you set, not from Umbraco's idea of its URL: `Simon Antony (Production)`.
- **It goes to one address or several.** Separated by commas, semicolons, spaces or new lines. Every address is validated and a bad one is reported, not silently dropped.
- **It never sends from Development by default.** A forgotten local copy stays quiet.
- **It doesn't nag.** A problem that hasn't changed is not emailed again each day. New problems are marked **NEW**, and you get a reminder every few days while one remains.
- **It tells you when things are fixed.** A short "all resolved" email when every reported problem has cleared.
- **You can snooze a known problem** until a date, with a reason. It drops out of the email, and the email says what is snoozed and warns when a snooze is about to end.
- **It can call a webhook** with a JSON payload, as well as or instead of sending email.
- **You can send a test alert** from the dashboard to prove delivery works.
- **It escapes what it emails.** Health check messages can contain HTML. Markup is stripped and the rest is encoded before it goes into the email.

A warnings-only run sends an email, which Umbraco's `FailureOnly` setting would not. Set the minimum severity to `Error` if you only want errors.

Slack is deliberately not supported. It would mean sending health check messages to a third-party service. The webhook covers that case if you want it, and the community [Slack notification method](https://github.com/CrumpledDog/Umbraco-HealthCheckSlackNotificationMethod) works alongside this package.

## Quick start

1. Install the package.
2. Add a `problemsEmail` entry beside Umbraco's own `email` one. Switch the built-in one off so you don't get two emails:

```json
{
  "Umbraco": {
    "CMS": {
      "HealthChecks": {
        "Notification": {
          "Enabled": true,
          "FirstRunTime": "0 4 * * *",
          "Period": "1.00:00:00",
          "NotificationMethods": {
            "email": {
              "Enabled": false,
              "Settings": { "RecipientEmail": "unused@example.com" }
            },
            "problemsEmail": {
              "Enabled": true,
              "Settings": {
                "RecipientEmail": "support@example.com, ops@example.com",
                "SiteName": "Example Ltd"
              }
            }
          }
        }
      }
    }
  }
}
```

`Enabled` here is the on/off switch for the method. Umbraco's own `Notification` block still decides whether the job runs, when it first runs and how often.

Keep `Settings.RecipientEmail` on the built-in `email` method even when it is disabled. Umbraco's own constructor for that method throws a `KeyNotFoundException` without it, which stops *all* notification methods from loading.

3. Open **Settings > Health Check Alerts** to change anything else.

## The dashboard

**Settings > Health Check Alerts** (administrators only) has:

- **Status.** When the last run happened and when an alert last went out, and the problems that run found. Each has a **Snooze** button (a date and a reason) and, once snoozed, an **Unsnooze**.
- **Email.** Recipients, site name, minimum severity, subject prefix, ignored environments, show environment, send when healthy.
- **Webhook.** URL, header name and key. The key is write-only: the dashboard shows only whether one is set, and you can replace or clear it.
- **Repeat.** `OnChange` or `EveryRun`, reminder days, send resolved.
- **Snoozes.** The list, with remove.
- **Send test alert.** Sends a made-up alert down every channel that is set up, using the saved settings. It ignores the ignored-environments list, because you asked for it. It reports what happened.

Saved changes apply from the next scheduled run. Nothing needs a restart or a deploy.

### Where settings come from

For each setting, the order is: **saved in the dashboard**, then **`appsettings.json`**, then the built-in default. The `problemsEmail` entry's `Settings` are only defaults, which is handy for a fresh install or for setting a value through an environment variable. The dashboard shows which fields are saved.

| Key | Default | What it does |
|---|---|---|
| `RecipientEmail` | none | One or more addresses, separated by commas, semicolons, spaces or new lines. Duplicates ignored, whatever their case. Invalid addresses are skipped with a warning in the log and the dashboard. With no valid address (and no webhook) nothing is sent. |
| `SiteName` | the application URL host, then `Umbraco site` | The name shown in the subject and body. |
| `MinimumSeverity` | `Warning` | `Warning` reports warnings and errors. `Error` ignores warnings. |
| `SendWhenHealthy` | `false` | `true` sends a short all-clear when there is nothing to report. |
| `SubjectPrefix` | `Health check:` | The start of every subject. Empty means no prefix. |
| `IgnoreEnvironments` | `Development` | ASP.NET Core environment names that never send. Empty means send everywhere. |
| `ShowEnvironment` | `true` | Adds the environment in brackets: `Simon Antony (Production)`. |
| `WebhookUrl` | none | Where to post the JSON. `https` only (plain `http` is accepted for localhost, for testing). |
| `WebhookAuthHeader` | `X-Api-Key` | The header that carries the key. |
| `WebhookAuthKey` | none | Sent as the value of that header. |
| `RepeatMode` | `OnChange` | `OnChange` or `EveryRun`. |
| `ReminderEveryDays` | `7` | With `OnChange`, remind about unchanged problems after this many days. `0` never reminds. |
| `SendResolved` | `true` | Send an "all resolved" alert when every reported problem has cleared. |

Snoozes can only be set in the dashboard (or through the API).

A bad value never stops the site. It falls back to the default and logs a warning.

The subject is `{SubjectPrefix} {summary} - {SiteName} ({Environment})`, for example `Health check: 2 errors, 2 warnings - Simon Antony (Production)`. The all-clear is `... all clear ...`, the resolved alert `... all resolved ...` and the test `... test alert ...`.

`Verbosity` and `FailureOnly` on the `problemsEmail` entry are ignored.

## Repeats, snoozes and resolved

The package remembers what it reported at the last run (in Umbraco's key/value table).

- **`OnChange`** (the default) sends when a problem is new, or when `ReminderEveryDays` have passed since the last alert while problems remain. A problem counts as the same if its check, severity and message are the same. New ones are marked **NEW** in the email.
- **`EveryRun`** sends on every run that has a problem.
- **Resolved.** When every reported problem has cleared, you get one short email naming what cleared. If only some clear and a new problem arrives, that email says what cleared since the last alert. Fixing one of two problems on its own sends nothing.
- **Snoozed** problems are left out of the email and of the record. Snoozing is muting, not fixing, so it is never announced as "resolved". The last day of a snooze is the date you set, and the email flags a snooze that ends within three days. When it ends, the problem counts as new.
- If an alert could not be delivered by any channel, the run is not recorded as reported and the next run tries again.

## Webhook

Set a URL (and ideally a key) and every alert is also posted as JSON. Email and webhook are independent: either, both or neither can be set, and one failing never blocks the other. Delivery has a 10 second timeout, and `https` is required except for localhost.

The key is sent in the header you name (`X-Api-Key` by default). It is never in the payload, and neither the key nor the URL is ever logged or shown in a failure message (webhook URLs often carry a secret).

```json
{
  "source": "Simplepage.Umbraco.HealthCheckAlerts",
  "status": "problems",
  "summary": "1 error, 1 warning",
  "site": "Simon Antony (Production)",
  "siteName": "Simon Antony",
  "environment": "Production",
  "host": "www.example.com",
  "runAt": "2026-11-09T08:59:00",
  "errors": 1,
  "warnings": 1,
  "passedChecks": 17,
  "problems": [
    {
      "check": "Content Security Policy (CSP)",
      "severity": "error",
      "message": "The header is missing.",
      "readMoreUrl": "https://docs.umbraco.com/",
      "isNew": true
    }
  ],
  "resolved": [],
  "snoozed": [ { "check": "Excessive Headers", "until": "2026-12-01", "reason": "fix in next release" } ]
}
```

`status` is `problems`, `resolved`, `all-clear` or `test`.

## Management API

Administrators only, at `/umbraco/management/api/v1/healthcheckalerts`, and part of the back office's own Swagger document.

| Method and path | What it does |
|---|---|
| `GET settings` | The settings in force, which fields are saved, warnings, and `webhookAuthKeySet`. Never the key itself. |
| `PUT settings` | Saves settings. Invalid values return 400 with an `errors` list and nothing is saved. A blank key keeps the saved key. `clearWebhookAuthKey: true` removes it. Leaving out `snoozes` keeps them. |
| `GET status` | Last run, last alert, and the problems found with their snooze state. |
| `POST snooze` | Snoozes a check: `{ "check", "until": "yyyy-MM-dd", "reason" }`. |
| `DELETE snooze?check=` | Removes a snooze. |
| `POST test` | Sends a test alert. Refused with 429 if called again within 10 seconds. |

## How it hooks into Umbraco

`ProblemsEmailNotificationMethod` derives from Umbraco's `NotificationMethodBase` and is marked `[HealthCheckNotificationMethod("problemsEmail")]`. Umbraco finds it and the package's composer when it starts, so installing the package is the registration. There is nothing to add to `Program.cs`.

- It reads results through the public `HealthCheckResults.GetResultsForStatus`, not Umbraco's internal dictionary.
- It sends through Umbraco's `IEmailSender`, so mail goes out over whatever SMTP your site already uses, from `Umbraco:CMS:Content:Notifications:Email`. Umbraco's sender doesn't split a comma-separated string and silently skips any address it can't parse, which is why the package splits and validates recipients itself.
- Settings and state are stored in Umbraco's key/value table, so there is no schema to migrate.
- The only network call of its own is the optional webhook.
- The rules (what to send, formatting, parsing, repeat logic) are plain C# with no Umbraco dependency (`Core/`) and are unit tested on their own.

## Known limits

- **It can only see what Umbraco's public API shows it.** It reports the checks Umbraco runs and the messages they give. It doesn't add checks of its own.
- **Email and webhook only.** No Slack, Teams or SMS.
- **Manual runs never send.** Running the health checks from the back office doesn't send a notification, in Umbraco or here. Only the scheduled job does.
- **Silence means healthy only while the job runs.** If the scheduled job isn't running, nothing is sent either. `SendWhenHealthy` is the way to notice that.
- **A problem whose message changes every run** (for example one that includes a count or a time) looks new each time, so it will be reported each time.
- **The webhook key is stored in the database in plain text.** See [SECURITY.md](SECURITY.md).

## Compatibility

| | |
|---|---|
| Umbraco | 17.x (built against the 17.0.0 floor, tested on 17.8.1) |
| .NET | 10 |

Works on **Umbraco Cloud, including the Starter plan**. It's an ordinary NuGet package from nuget.org, so there is no private feed to set up.

Umbraco 13 and 14 to 16 are not supported.

**Not yet verified:** newer 17.x releases than 17.8.1, and installing the packed `.nupkg` into a separate site (the test site references the package from source). Real mouse clicks on the dashboard's snooze form have not been tried either, only scripted events.

## FAQ

**Do I need to deploy to change recipients?** No. Save them in the dashboard. They apply from the next run.

**How does it work on Umbraco Cloud?** There is nothing per-deploy to configure beyond switching the method on. Saved settings live in the database, which Umbraco Deploy does not carry between environments, so each environment (Development, Live) has its own. That is usually what you want: Live emails support and Development stays quiet.

**Where should the webhook key go on Cloud?** Use a Cloud secret or environment variable rather than the dashboard if you can:
`Umbraco__CMS__HealthChecks__Notification__NotificationMethods__problemsEmail__Settings__WebhookAuthKey`. A key saved in the dashboard overrides it.

**Why not Slack?** It sends your health check messages to a third party. If you want it, the webhook can post to anything, and the community Slack method works alongside this package.

**I get two emails.** Switch Umbraco's own `email` method off (`"Enabled": false`).

**Why did a problem not arrive today?** Because it hasn't changed since the last alert. See [Repeats, snoozes and resolved](#repeats-snoozes-and-resolved), or set `RepeatMode` to `EveryRun`.

## Contributing

We want two things, in this order:

1. **Security problems.** Anything that lets a health check message inject markup into the email, or puts a value into an email or webhook that you didn't expect. Please report these privately: see [SECURITY.md](https://github.com/SimplepageUK/healthcheckalerts/blob/main/SECURITY.md).
2. **Findings that are mishandled.** A real health check result that the email shows badly: lost, garbled or in the wrong place. Add it as a test case and open a pull request.

See [CONTRIBUTING.md](https://github.com/SimplepageUK/healthcheckalerts/blob/main/CONTRIBUTING.md) for how to run the tests.

## Credits

Umbraco's own `EmailNotificationMethod` and `NotificationMethodBase` are the reference for how a notification method is built. [Our.Umbraco.HealthCheckSlackNotificationMethod](https://github.com/CrumpledDog/Umbraco-HealthCheckSlackNotificationMethod) and [Umbraco.Community.Cloud.HealthChecks](https://github.com/CrumpledDog/Umbraco.Community.Cloud.HealthChecks) (disk, NuGet cache and log-folder checks for Umbraco Cloud) both work alongside this package.

## Licence

MIT. Copyright (c) 2026 Simplepage Ltd.
