using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Cms.Core.HealthChecks.NotificationMethods;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddComposers()
    .Build();

WebApplication app = builder.Build();

await app.BootUmbracoAsync();

app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.UseBackOffice();
        u.UseWebsite();
    })
    .WithEndpoints(u =>
    {
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();
    });

// Umbraco only runs its notification methods from a scheduled background job, and a manual run from the
// dashboard never sends them. This runs every health check and then every enabled notification method,
// exactly as the scheduled job does, so tests/smoke/run.sh does not have to wait for the schedule.
// Development only, or when HEALTHALERTS_TEST_ENDPOINT=1, which the smoke test sets to try Staging.
// This site is never deployed.
if (app.Environment.IsDevelopment() || Environment.GetEnvironmentVariable("HEALTHALERTS_TEST_ENDPOINT") == "1")
{
    app.MapPost("/dev/run-health-notifications", async (HealthCheckCollection checks, HealthCheckNotificationMethodCollection methods) =>
    {
        HealthCheckResults results = await HealthCheckResults.Create(checks);

        var report = new List<object>();
        foreach (IHealthCheckNotificationMethod method in methods)
        {
            report.Add(new { method = method.GetType().Name, enabled = method.Enabled });
            if (method.Enabled)
            {
                await method.SendAsync(results);
            }
        }

        return Results.Json(new { allChecksSuccessful = results.AllChecksSuccessful, methods = report });
    });
}

await app.RunAsync();
