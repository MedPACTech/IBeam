# IBeam.Licensing

Core contracts and models for tenant application licensing in IBeam-backed applications.

Install this package when you need licensing contracts in shared application code:

```powershell
dotnet add package IBeam.Licensing
```

Use `IBeam.Licensing.Services` for runtime orchestration and `IBeam.Licensing.Api` for ASP.NET Core endpoint wiring.

Licensing is intentionally separate from Identity. Identity should identify the user, tenant, API credential, or agent and enforce roles/scopes. Licensing answers whether that tenant has purchased or been granted a feature entitlement and whether the subject consumes a licensed seat.

Core concepts:

- License plans define default entitlements and limits.
- Tenant licenses grant a plan to a tenant, optionally with provider references and overrides.
- Seat assignments link a license to a user, API credential, agent, or external subject.
- `ILicenseAuthorizer` checks runtime access to feature entitlements.

Typical service-layer usage:

```csharp
public sealed class WorkCardService
{
    private readonly ILicenseAuthorizer _licenses;

    public WorkCardService(ILicenseAuthorizer licenses)
    {
        _licenses = licenses;
    }

    public async Task CreateCardAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        await _licenses.RequireEntitlementAsync(
            tenantId,
            new LicenseSubject(LicenseSubjectTypes.User, userId.ToString()),
            "work:cards:create",
            ct);

        // Continue with the licensed operation.
    }
}
```

Identity and Licensing should usually be checked together:

```csharp
await _roleAccess.AuthorizeAsync(user, "work:write", ct);
await _licenses.RequireEntitlementAsync(tenantId, subject, "work:cards:create", ct);
```

For API credential and MCP scenarios, treat the API credential scope as the authenticated permission and the license entitlement as the purchased capability:

```text
API credential scope: api-scope:work
License entitlement: work:cards:create
```
