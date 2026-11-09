using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Simplepage.Umbraco.HealthCheckAlerts.Api;
using Swashbuckle.AspNetCore.SwaggerGen;
using Umbraco.Cms.Api.Management.OpenApi;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace Simplepage.Umbraco.HealthCheckAlerts.Composing;

/// <summary>Gives the API its own Swagger document, so it shows in the Management API's Swagger UI and is secured the same way.</summary>
public sealed class HealthCheckAlertsSwaggerComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder) =>
        builder.Services.ConfigureOptions<ConfigureHealthCheckAlertsSwagger>();

    private sealed class ConfigureHealthCheckAlertsSwagger : IConfigureOptions<SwaggerGenOptions>
    {
        public void Configure(SwaggerGenOptions options)
        {
            options.SwaggerDoc(Constants.ApiName, new OpenApiInfo
            {
                Title = "Health Check Alerts API",
                Version = "Latest",
                Description = "Settings, status and test alert for the Health Check Alerts dashboard.",
            });

            options.OperationFilter<SecurityFilter>();
        }
    }

    private sealed class SecurityFilter : BackOfficeSecurityRequirementsOperationFilterBase
    {
        protected override string ApiName => Constants.ApiName;
    }
}
