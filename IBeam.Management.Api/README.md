# IBeam.Management.Api

Reusable portal API library. It does not run a server, own an identity database, or install login endpoints.

The host must first register a dedicated support authentication scheme. It must validate a management-only audience/trust domain and issue management context only for enabled, MFA-completed support members. Ordinary customer tokens must not be accepted by this scheme.

```csharp
// Register "ManagementSupport" with the host's trusted authentication implementation first.
// Configure its management-only token audience/issuer and membership/MFA checks.
builder.Services.AddIBeamManagementApi(builder.Configuration,
    options => options.AuthenticationScheme = "ManagementSupport");

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
```

Equivalent setting: `IBeam:Management:Api:AuthenticationScheme`. Blank or unregistered schemes fail startup. Registration does not change the host's authentication defaults.

`GET /api/management/me` returns an IBeam success envelope containing `userId` and `managementOrganizationId`. The support scheme must establish exactly one authenticated identity containing:
- `uid`, `sub` or name identifier (aliases must agree).
- `ibeam_management_subject_type=human`.
- `ibeam_management_organization_id=<nonempty GUID>`.
- `ibeam_management_permission=management.identity.read`.

Missing/wrong authentication is 401. Authenticated but invalid management context is 403 with a safe IBeam error envelope. No claims or credential details are included in denial responses.

This first package does not implement app registration, app assignments, login/MFA or diagnostic reads. IBeam.Admin.API will later consume published packages as a runnable test instance; that repository is intentionally unchanged.
