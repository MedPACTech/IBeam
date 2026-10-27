using System.Net;
using System.Text.Json;
using IBeam.Management.Services;
using IBeam.Management.Services.Credentials;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Management.Operations.Target.Api;

public static class ManagementRegisteredTargetApiExtensions
{
    private sealed class Registration;
    public static IServiceCollection AddIBeamManagementRegisteredOperationsTargetApi(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(x => x.ServiceType == typeof(Registration))) return services;
        services.AddSingleton<Registration>(); services.AddIBeamManagementRegisteredOperationsTarget(configuration);
        services.AddOptions<ManagementCredentialNetworkOptions>().Bind(configuration.GetSection(ManagementCredentialNetworkOptions.SectionName))
            .Validate(o => o.AllowedNetworks is { Count: <= 100 } && o.AllowedNetworks.All(x => IPNetwork.TryParse(x, out _)), "At most 100 valid CIDR networks are supported.").ValidateOnStart();
        services.TryAddSingleton<ManagementCredentialNetworkPolicy>(); services.TryAddScoped<IManagementCredentialAuthenticationService, ManagementCredentialAuthenticationService>();
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, ManagementRegisteredCredentialAuthentication>(ManagementRegisteredAuthentication.Scheme, _ => { });
        services.AddAuthorizationBuilder().AddPolicy(ManagementRegisteredAuthentication.Policy, policy => policy.AddAuthenticationSchemes(ManagementRegisteredAuthentication.Scheme).RequireAuthenticatedUser());
        services.AddHttpContextAccessor(); services.TryAddScoped<IManagementPrincipalProvider, Principal>(); return services;
    }
    public static IEndpointRouteBuilder MapIBeamManagementRegisteredOperations(this IEndpointRouteBuilder endpoints)
    {
        var registry = endpoints.ServiceProvider.GetRequiredService<ManagementOperationRegistry>();
        endpoints.MapGet("/api/management-operations/info", async (HttpContext context, IManagementRegisteredOperationInfoService service) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            try { return Results.Json(new { success = true, data = await service.GetAsync(context.RequestAborted) }); }
            catch (Exception ex) when (Status(ex) != 0) { return Error(Status(ex)); }
        }).RequireAuthorization(ManagementRegisteredAuthentication.Policy).WithMetadata(new HttpLoggingAttribute(HttpLoggingFields.None));
        foreach (var descriptor in registry.Descriptors)
        {
            var operation = descriptor.Operation;
            endpoints.MapMethods(descriptor.RouteTemplate, [descriptor.Method], (HttpContext context, IManagementRegisteredOperationDispatcher dispatcher) =>
                Execute(context, dispatcher, operation, false, descriptor.MaximumRequestBytes))
                .RequireAuthorization(ManagementRegisteredAuthentication.Policy).WithMetadata(new HttpLoggingAttribute(HttpLoggingFields.None));
        }
        endpoints.MapGet("/api/management-operations/receipts/{operationId:guid}", (HttpContext context, IManagementRegisteredOperationDispatcher dispatcher) =>
            Execute(context, dispatcher, "", true, 0)).RequireAuthorization(ManagementRegisteredAuthentication.Policy).WithMetadata(new HttpLoggingAttribute(HttpLoggingFields.None));
        return endpoints;
    }
    private static async Task<IResult> Execute(HttpContext context, IManagementRegisteredOperationDispatcher dispatcher, string operation, bool receipt, int limit)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            if (context.Request.QueryString.HasValue || context.Request.ContentLength > limit) throw new ManagementOperationInvalidException();
            using var body = new MemoryStream(); var chunk = new byte[4096];
            while (true) { var read = await context.Request.Body.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, limit + 1 - (int)body.Length)), context.RequestAborted);
                if (read == 0) break; body.Write(chunk, 0, read); if (body.Length > limit) throw new ManagementOperationInvalidException(); }
            string? Header(string name, bool required = false)
            { var value = context.Request.Headers[name]; if (value.Count > 1 || required && value.Count != 1) throw new ManagementOperationInvalidException(); return value.Count == 0 ? null : value[0]; }
            var result = await dispatcher.ExecuteAsync(new() { Operation = operation, Method = context.Request.Method, Path = context.Request.Path.Value!,
                Body = body.ToArray(), IdempotencyKey = Header("Idempotency-Key"), IfMatch = Header("If-Match"),
                Assertion = Header("X-IBeam-Management-Actor", true)!, ReceiptLookup = receipt }, context.RequestAborted);
            using var json = JsonDocument.Parse(result.Body); return Results.Json(new { success = true, data = json.RootElement.Clone() }, statusCode: result.Unknown ? 202 : 200);
        }
        catch (Exception ex) when (Status(ex) != 0) { return Error(Status(ex)); }
    }
    private static int Status(Exception ex) => ex switch { ManagementAccessDeniedException => 403, ManagementOperationInvalidException => 400,
        ManagementOperationConflictException => 409, ManagementOperationPreconditionException => 412, ManagementOperationUnavailableException or ManagementAuditUnavailableException => 503, _ => 0 };
    private static IResult Error(int status) => Results.Json(new { success = false, errors = new[] { new { code = "Management.OperationRequestFailed",
        message = status switch { 400 => "The management operation request is invalid.", 403 => "Management access is not permitted.", 409 => "The management operation conflicts with current state.",
            412 => "The resource version changed.", _ => "The management operation is unavailable." } } } }, statusCode: status);
    private sealed class Principal(IHttpContextAccessor context) : IManagementPrincipalProvider
    { public System.Security.Claims.ClaimsPrincipal GetPrincipal() => context.HttpContext?.User ?? new(); }
}
