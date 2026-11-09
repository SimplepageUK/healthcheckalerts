# Security policy

## Reporting a problem

If you have found a way to make Health Check Alerts email something it should not, **please don't open a public issue**. Report it privately:

**[Report a vulnerability](https://github.com/SimplepageUK/healthcheckalerts/security/advisories/new)** (GitHub private vulnerability reporting)

Please include:

- what the email contained, and what it should have contained
- for a markup problem: the health check message that caused it, and which email client rendered it
- for a leak: which setting or value appeared in the email
- the package version, the Umbraco version, and any non-default configuration

## What happens next

- We acknowledge reports within 3 working days.
- We aim to release a fix within 14 days of confirming a problem, sooner for anything that lets a message inject markup.
- The sample goes into the test suite, so the fix stays fixed.
- With your permission we credit you in the release notes and the advisory.

## Supported versions

Only the latest minor version receives security fixes.

| Version | Supported |
|---|---|
| 1.x | Yes |

## Scope

What the package does: after Umbraco's scheduled health checks run, it emails the names and messages of the checks that found a warning or an error, and optionally posts them as JSON to a webhook. Recipients and the webhook come from settings saved by an administrator in the back office, or from configuration. Email goes through Umbraco's own `IEmailSender`. The webhook is the only network call the package makes itself.

How it handles what is sensitive:

- **Settings are admin-only.** The dashboard and the Management API (`/umbraco/management/api/v1/healthcheckalerts`) require Umbraco's administrator policy. Anyone who can change them can change who is emailed and where the webhook posts, so treat that access accordingly. The test-alert endpoint is rate limited to one call every 10 seconds.
- **The webhook key is stored in plain text** in Umbraco's key/value table (database), like other settings saved in the back office. It is write-only: the API and dashboard report only whether a key is set, and never return it. It is never logged and never appears in the payload or in an email. If you would rather not keep it in the database, set it through an environment variable or Umbraco Cloud secret (`Umbraco__CMS__HealthChecks__Notification__NotificationMethods__problemsEmail__Settings__WebhookAuthKey`); a key saved in the dashboard overrides that value.
- **The webhook URL is treated as secret too**, because such URLs often carry a token. Failure messages and logs name only the host. `https` is required, except for loopback addresses so it can be tested. URLs with embedded credentials are refused, as are header names with anything but letters, digits and hyphens, and keys containing line breaks. Delivery times out after 10 seconds.
- **Everything in the email is encoded.** Health check messages, check names, the site name, the host and snooze reasons are HTML-encoded, and tags in health check messages are stripped first. "Read more" links are only ever plain `http` or `https` URLs, so `javascript:` and `data:` links are dropped.
- **Headers can't be injected.** The subject collapses line breaks and control characters from `SiteName` and `SubjectPrefix`. Recipients must be plain addresses, so a value containing `Bcc:` or a line break is refused.
- **Ignored environments never send** scheduled alerts (`Development` by default). The one exception is the explicit test button.

In scope:

- a health check message that injects markup into the email, or gets past the HTML escaping
- a setting that an editor or visitor could change which alters who receives the email, or any way to reach the API without administrator access
- a value, such as a connection string, key or password, that appears in an email, a webhook payload, an API response or a log when it should not (including the webhook key or URL)
- a subject or header built from `SiteName` or `SubjectPrefix` that can be split into extra headers (header injection)
- a webhook URL that gets past the checks above, or a way to make the package post somewhere the administrator did not set
- an instance in an ignored environment (`IgnoreEnvironments`, `Development` by default) that still sends a scheduled alert

Out of scope: the health checks themselves and what their messages say (report those to [Umbraco](https://umbraco.com/about-us/trust-center/security-and-umbraco/how-to-report-a-vulnerability-in-umbraco/) or the package that provides the check), the security of your SMTP server or webhook receiver, and other notification methods such as Slack.
