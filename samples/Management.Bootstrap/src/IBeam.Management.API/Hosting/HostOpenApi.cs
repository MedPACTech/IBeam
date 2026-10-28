using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;

namespace IBeam.Management.API.Hosting;

public static class HostOpenApi
{
    public static string PackageVersion => typeof(IBeam.Management.Entities.ManagementApplicationEntity).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown";

    public static IServiceCollection AddManagementOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "IBeam Management API";
                document.Info.Version = PackageVersion;
                document.Info.Description = "Support metadata only. Optional routes appear only when explicitly installed. " +
                    "Use a support session obtained through this host's normal sign-in and MFA flow. " +
                    "Customer JWTs and target machine keys are not support sessions.";
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes["SupportBearer"] = new OpenApiSecurityScheme
                { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", Description = "Support access token after normal MFA. Never enter a target machine key." };
                return Task.CompletedTask;
            });
            options.AddOperationTransformer((operation, context, _) =>
            {
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;
                if (!metadata.OfType<IAllowAnonymous>().Any() && metadata.OfType<IAuthorizeData>().Any())
                {
                    operation.Security ??= [];
                    operation.Security.Add(new OpenApiSecurityRequirement
                    { [new OpenApiSecuritySchemeReference("SupportBearer", context.Document)] = [] });
                }
                return Task.CompletedTask;
            });
        });
        return services;
    }
}
