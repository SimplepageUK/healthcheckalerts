# Contributing

Thank you. The tests matter more than the code, so real health check results that the email gets wrong are the most useful thing you can send.

## 1. Security problems

Anything that lets a health check message inject markup into the email, puts a setting into an email that should not be there, or sends from an environment it should have skipped. **Report these privately** through [SECURITY.md](SECURITY.md), not as a pull request. Once a fix ships, the sample joins the test cases with credit to you.

## 2. Findings that should not be emailed like that

A real health check result, from Umbraco or from a package, that the email shows badly: a message that is cut off, garbled, shown as raw markup, or put under the wrong heading. To add one:

1. Add a case to the formatter tests in `tests/Simplepage.Umbraco.HealthCheckAlerts.Tests/`. Give it the check name, the severity and the exact message Umbraco produced, and say what the email line should be.
2. Run the tests. It will fail - that is the point of the pull request.
3. Open a pull request saying which check produced it, in which Umbraco or package version.

Please remove anything confidential from the message first, such as hostnames, addresses and keys. A simplified message that reproduces the problem is ideal.

We won't accept every request to change what is shown. Some choices are deliberate (see the README's known limits), but each one gets a documented answer.

## Adding a test case

Cases in the test project's `Program.cs` are `Check("what should be true", condition)` or `Same("what", expected, actual)` lines, grouped under `Section(...)` headings. Name the rule in plain words, so a failure says which rule broke and why it matters. A case that passes for the wrong reason proves nothing about the rule it was written for.

```
Check("a check message containing a script tag is encoded, not rendered", body.Contains("&lt;script&gt;"));
```

## Running the tests

```
dotnet build -c Release
dotnet run --project tests/Simplepage.Umbraco.HealthCheckAlerts.Tests -c Release --no-build
```

It prints each case and ends with `N passed, 0 failed` (316 at the time of writing). It exits non-zero on any failure. The unit tests use fakes for the Umbraco services around the notification method, and real Umbraco `HealthCheckResults` built from fake checks.

## Testing in a real back office

The unit tests prove the formatting and the settings. They don't prove the Umbraco wiring, so any change to the notification method itself needs a real run. `tests/Simplepage.Umbraco.HealthCheckAlerts.TestSite` is a plain Umbraco 17 site with the package referenced from source. Its SQLite database stays on your machine and is not committed, and mail is never sent: it writes each email to a pickup folder as an `.eml` file you can open.

The quick way is the smoke test, which does the whole run for you:

```
bash tests/smoke/run.sh
```

It boots the test site on a throwaway SQLite database, triggers the notification methods the way the scheduled job does (through a dev-only endpoint), and checks the `.eml` files and the calls a local webhook listener receives. It needs `dotnet`, `python3`, `curl` and `lsof`. CI runs it on Linux. Scenarios:

- **A.** Development sends nothing by default.
- **B.** Development with `IgnoreEnvironments` switched off, a webhook and a key: one email to both recipients with the right subject, one webhook call carrying the key, and a second run that sends nothing more.
- **C.** After a restart the same problems are still not repeated, because the record of what was reported survived.
- **D.** Staging is not ignored, and `RepeatMode=EveryRun` repeats the unchanged problems with a `(Staging)` subject.

The dashboard and the Management API are not covered by the script, because a backoffice token needs a browser login. Check them by hand in the test site (below) after changing `Api/` or `wwwroot/`.

To look around by hand:

```
dotnet run --project tests/Simplepage.Umbraco.HealthCheckAlerts.TestSite
```

It opens https://localhost:44357/umbraco. The site installs itself on first start with a **test-only admin login** written in the test site's `appsettings.Development.json`. That login is for the local copy only and is not a secret, and CI allows exactly that one password to be committed. Then open **Settings > Health Check Alerts**. A scheduled-style run, `POST /dev/run-health-notifications` (Development only), should leave an email in the pickup folder listing only the warnings and errors.

**Don't commit anything else the installer writes into `appsettings.json`.** It can add `Umbraco:CMS:Global:Id` and a generated `Imaging:HMACSecretKey`. Discard those changes before committing; CI fails if a key or a password is committed.

The Management API is protected by the back office login (OpenIddict), so calling it from a script needs a browser sign-in first. The dashboard is a hand-written ES module with no build step: `src/Simplepage.Umbraco.HealthCheckAlerts/wwwroot/App_Plugins/Simplepage.HealthCheckAlerts/`.

## Packaging notes

- The project uses `Microsoft.NET.Sdk.Razor` with `StaticWebAssetBasePath` set to `/`, so the dashboard files ship as static web assets and are served at `/App_Plugins/Simplepage.HealthCheckAlerts/`.
- It references `Umbraco.Cms.Core` and `Umbraco.Cms.Api.Management`, both `[17.0.0, 18.0.0)`. 17.0.0 is the floor we compile against, so a newer API sneaking in breaks the build. For example, the three-argument `IEmailSender.SendAsync` is used because the four-argument one arrived later.
- `NuGetAuditMode` is `direct`. The 17.0.0 floor pulls in transitive packages (MailKit, MessagePack, Microsoft.OpenApi) that have since had advisories, and warnings are errors here. Those packages belong to Umbraco and a site gets patched ones with its own Umbraco version, so only packages we reference directly are audited.

## How the test project is built

`src/Simplepage.Umbraco.HealthCheckAlerts/Core/` holds the rules: what counts as a problem worth sending, repeat and snooze logic, formatting, parsing and validation. It must never reference Umbraco, ASP.NET Core or `Microsoft.Extensions` (the BCL, including `System.Text.Json`, is fine). It is plain C# so it can be tested without a site running. If something in `Core/` needs an Umbraco type, the Umbraco side should pass in plain values instead. CI and a unit test both check this: a `using Umbraco`, `using Microsoft.AspNetCore`, `using Microsoft.Extensions` or an `Umbraco.Cms.` reference anywhere in `Core/` fails the build.

The rest of the package:

- `Notifications/` is the notification method and the collector that reads Umbraco's results.
- `Services/` is the store (Umbraco key/value), the runtime that works out the settings in force, the dispatcher and the webhook sender.
- `Composing/` registers the services and the Swagger document.
- `Api/` is the admin-only Management API.
- `wwwroot/` is the dashboard.

## Code

- `Core/` is pure and Umbraco-free. The Umbraco wiring is in `Notifications/`, `Services/` and `Api/`.
- Never log, return or put in a message the webhook key or URL.
- Send nothing when nothing is wrong. A noisy alert gets filtered, and then it is worse than none.
- Escape everything that goes into the email. A health check message is data from somewhere else, not markup you wrote.
- Every behaviour change comes with a test case that proves it, with the reason named.
- UK English in docs and messages.

## Working on the dashboard

- Browsers cache `dashboard.js` without revalidating, so a hard refresh may keep running the old script. The script URL in `umbraco-package.json` carries `?v=<package version>`, and a unit test checks it matches the csproj `<Version>`. Bump both on every release. While editing the script, tick "Disable cache" in the browser's developer tools, or change the `?v=` value temporarily.
- Do not wrap UUI controls in a `<label>` and do not set `display: block` on `uui-input`. Both stop the input taking a click or showing its value. Unit tests guard against both. Use `<uui-label>` and leave the input's display alone.
- The test site needs a `mail` folder next to it for the pickup directory. Without it a test alert fails with `DirectoryNotFoundException`.
