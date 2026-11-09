using Microsoft.Extensions.DependencyInjection;
using Simplepage.Umbraco.HealthCheckAlerts.Services;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace Simplepage.Umbraco.HealthCheckAlerts.Composing;

/// <summary>Registers the package's services. Umbraco finds composers by scanning, so there is nothing for the site to add.</summary>
public sealed class HealthCheckAlertsComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddHttpClient(WebhookSender.HttpClientName);
        builder.Services.AddSingleton<IAlertStore, KeyValueAlertStore>();
        builder.Services.AddSingleton<IWebhookSender, WebhookSender>();
        builder.Services.AddSingleton<AlertRuntime>();
        builder.Services.AddSingleton<AlertDispatcher>();
    }
}
