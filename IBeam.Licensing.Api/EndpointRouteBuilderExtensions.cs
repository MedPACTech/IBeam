using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace IBeam.Licensing.Api;

public static class LicensingEndpointRouteBuilderExtensions
{
    public static RouteGroupBuilder MapIBeamLicensing(
        this IEndpointRouteBuilder endpoints,
        string prefix = "/api",
        string? authorizationPolicy = null)
    {
        var group = endpoints.MapGroup(prefix);

        if (string.IsNullOrWhiteSpace(authorizationPolicy))
            group.RequireAuthorization();
        else
            group.RequireAuthorization(authorizationPolicy);

        group.MapGet("/license-plans", async (
            ILicensePlanCatalogProvider plans,
            CancellationToken ct) =>
        {
            var result = await plans.ListPlansAsync(ct).ConfigureAwait(false);
            return Results.Ok(result);
        });

        group.MapGet("/tenants/{tenantId:guid}/licenses", async (
            Guid tenantId,
            ITenantLicenseService licenses,
            CancellationToken ct) =>
        {
            try
            {
                var result = await licenses.ListTenantLicensesAsync(tenantId, ct).ConfigureAwait(false);
                return Results.Ok(result);
            }
            catch (LicensingException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        group.MapPost("/tenants/{tenantId:guid}/licenses", async (
            Guid tenantId,
            GrantTenantLicenseRequest request,
            HttpContext httpContext,
            ITenantLicenseService licenses,
            CancellationToken ct) =>
        {
            try
            {
                var result = await licenses.GrantLicenseAsync(tenantId, request, ResolveUserId(httpContext.User), ct)
                    .ConfigureAwait(false);
                return Results.Ok(result);
            }
            catch (LicensingException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        group.MapPut("/tenants/{tenantId:guid}/licenses/{licenseId:guid}", async (
            Guid tenantId,
            Guid licenseId,
            UpdateTenantLicenseRequest request,
            ITenantLicenseService licenses,
            CancellationToken ct) =>
        {
            try
            {
                var result = await licenses.UpdateLicenseAsync(tenantId, licenseId, request, ct).ConfigureAwait(false);
                return Results.Ok(result);
            }
            catch (LicensingException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        group.MapPost("/tenants/{tenantId:guid}/licenses/{licenseId:guid}/revoke", async (
            Guid tenantId,
            Guid licenseId,
            RevokeTenantLicenseRequest request,
            ITenantLicenseService licenses,
            CancellationToken ct) =>
        {
            try
            {
                await licenses.RevokeLicenseAsync(tenantId, licenseId, request?.Reason, ct).ConfigureAwait(false);
                return Results.NoContent();
            }
            catch (LicensingException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        group.MapGet("/tenants/{tenantId:guid}/licenses/{licenseId:guid}/assignments", async (
            Guid tenantId,
            Guid licenseId,
            ILicenseSeatAssignmentService assignments,
            CancellationToken ct) =>
        {
            try
            {
                var result = await assignments.ListAssignmentsAsync(tenantId, licenseId, ct).ConfigureAwait(false);
                return Results.Ok(result);
            }
            catch (LicensingException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        group.MapPost("/tenants/{tenantId:guid}/licenses/{licenseId:guid}/assignments", async (
            Guid tenantId,
            Guid licenseId,
            AssignLicenseSeatRequest request,
            HttpContext httpContext,
            ILicenseSeatAssignmentService assignments,
            CancellationToken ct) =>
        {
            try
            {
                var result = await assignments.AssignSeatAsync(tenantId, licenseId, request, ResolveUserId(httpContext.User), ct)
                    .ConfigureAwait(false);
                return Results.Ok(result);
            }
            catch (LicensingException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        group.MapDelete("/tenants/{tenantId:guid}/licenses/{licenseId:guid}/assignments/{assignmentId:guid}", async (
            Guid tenantId,
            Guid licenseId,
            Guid assignmentId,
            ILicenseSeatAssignmentService assignments,
            CancellationToken ct) =>
        {
            try
            {
                await assignments.RevokeSeatAsync(tenantId, licenseId, assignmentId, ct).ConfigureAwait(false);
                return Results.NoContent();
            }
            catch (LicensingException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        group.MapGet("/tenants/{tenantId:guid}/license-entitlements", async (
            Guid tenantId,
            ITenantLicenseService licenses,
            CancellationToken ct) =>
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var tenantLicenses = await licenses.ListTenantLicensesAsync(tenantId, ct).ConfigureAwait(false);
                var entitlements = tenantLicenses
                    .Where(x => string.Equals(x.Status, LicenseStatuses.Active, StringComparison.OrdinalIgnoreCase) &&
                                x.RevokedUtc is null &&
                                x.StartsUtc <= now &&
                                (x.ExpiresUtc is null || x.ExpiresUtc > now))
                    .SelectMany(x => x.Entitlements)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return Results.Ok(new { tenantId, entitlements });
            }
            catch (LicensingException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        group.MapPost("/tenants/{tenantId:guid}/license-entitlements/check", async (
            Guid tenantId,
            CheckLicenseEntitlementRequest request,
            ILicenseAuthorizer authorizer,
            CancellationToken ct) =>
        {
            try
            {
                var result = await authorizer.AuthorizeAsync(tenantId, request.Subject, request.Entitlement, ct)
                    .ConfigureAwait(false);
                return Results.Ok(result);
            }
            catch (LicensingException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        return group;
    }

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue("uid") ??
                  user.FindFirstValue(ClaimTypes.NameIdentifier) ??
                  user.FindFirstValue("sub");

        return Guid.TryParse(raw, out var parsed) && parsed != Guid.Empty ? parsed : null;
    }
}
