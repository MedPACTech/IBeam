# IBeam.Identity.Services

Core identity orchestration package for OTP, password, OAuth, tokens, and tenant selection.

## Narrative Introduction

This package is where identity behavior is implemented. It consumes contracts from `IBeam.Identity` and composes authentication workflows while delegating storage and delivery concerns to repository and communications providers.

## Features and Components

- auth flow implementations:
  - `PasswordAuthService`
  - `OtpAuthService`
  - `OAuthAuthService`
- supporting services:
  - `OtpService`
  - `JwtTokenService`
  - `TenantSelectionService`
  - `TenantInviteService`
  - `IdentityCommunicationAdapter`
  - `PermissionAccessAuthorizer` (dynamic permission map authorization)
  - `PermissionCatalogProvider` (exposed permission catalog discovery)
  - `IBeamAccessControlService` (roles, permissions, grants, modules, resources, and current access context)
- DI extension methods:
  - `AddIBeamIdentityServices(IConfiguration)`
  - `AddIBeamAccessControl(...)`
  - `AddIBeamIdentityPermissionMappings(...)`
  - `AddIBeamIdentityPermissionCatalog(...)`
  - `AddIBeamIdentityAuthPasswordService()`
  - `AddIBeamIdentityAuthOtpService()`
  - `AddIBeamIdentityAuthOAuthService()`
  - `AddIBeamAuthEvents(...)`

## Cross-Pattern Auth Orchestration

`IBeam.Identity.Services` lets one user move between auth patterns without creating duplicate users. The service layer always works against `UserId` after the repository resolves an auth identifier.

Supported flows:

- OTP with SMS: `StartOtpAsync(phone)` then `CompleteOtpAsync(...)`.
- OTP with email: `StartOtpAsync(email)` then `CompleteOtpAsync(...)`.
- Email/password: `StartEmailPasswordRegistrationAsync(...)`, `CompleteEmailPasswordRegistrationAsync(...)`, then `PasswordLoginAsync(...)`.
- Add email/password to an existing SMS user: `StartEmailPasswordLinkAsync(...)`, then `CompleteEmailPasswordLinkAsync(...)`.
- Add SMS to an existing email user: `StartPhoneLinkAsync(...)`, then `CompletePhoneLinkAsync(...)`.
- 2FA: `StartTwoFactorSetupAsync(...)`, `CompleteTwoFactorSetupAsync(...)`, then `CompleteTwoFactorLoginAsync(...)`.
- Tenant invites: `CreateInviteAsync(...)`, `PreviewInviteAsync(...)`, and `AcceptInviteAsync(...)` for tenant-managed onboarding.

Successful OTP completion confirms the verified identity channel on the user record. Email OTP sets `IdentityUser.EmailConfirmed = true`; SMS OTP sets `IdentityUser.PhoneConfirmed = true` through the repository provider's confirmation fields.

The repository provider is responsible for fast identifier resolution. For Azure Table, this is done by an `AuthIdentifiers` table keyed by identifier type and normalized value.

## Dependencies

- Internal packages:
  - `IBeam.Identity`
  - `IBeam.Communications`
- External packages:
  - `Microsoft.Extensions.Configuration.Abstractions`
  - `Microsoft.Extensions.Caching.Abstractions`
  - `Microsoft.Extensions.Http`
  - `Microsoft.Extensions.Options`
  - `Microsoft.Extensions.Options.ConfigurationExtensions`
  - `Microsoft.Extensions.Identity.Stores`
  - `System.IdentityModel.Tokens.Jwt`

## Required Configuration

- `IBeam:Identity:Jwt`
- `IBeam:Identity:Otp`
- `IBeam:Identity:Features`
- `IBeam:Identity:OAuth` (when OAuth is enabled)
- `IBeam:Identity:Events` (optional)
- `IBeam:Identity:TenantProvisioning` (optional; auth tenant creation/linking policy)
- `IBeam:Identity:PermissionAccess` (optional; JSON permission map source)
- `IBeam:Identity:RoleManagement` (optional; tenant/admin policy toggles)
- `IBeam:Identity:AccessControl` (optional; module, access-level, and default role policy configuration)

### OTP Auto-Provision Toggle

- `IBeam:Identity:Otp:AllowAutoProvisionForUnknownUser`
  - `true`: OTP sign-in may create users for unknown destinations
  - `false`: unknown destinations are blocked in OTP start/complete flows
- Default when omitted:
  - `Development`: `true`
  - `Test` / `Production`: `false`
- Environment-variable override:
  - `IBeam__Identity__Otp__AllowAutoProvisionForUnknownUser=true|false`

### Tenant Provisioning Policy

`IBeam:Identity:TenantProvisioning:Mode` controls tenant behavior after OTP, password, and OAuth authentication resolves a user.

- `AutoCreateTenantForNewUser`: default/current behavior; creates a tenant/workspace when the authenticated user has no active membership.
- `RequireExistingTenant`: never creates or links a tenant from auth; missing membership fails with a validation error.
- `UseDefaultTenant`: uses `DefaultTenantId` when auth requests omit tenant id.

For `UseDefaultTenant`, set `AutoLinkUserToDefaultTenant` to `true` when IBeam should link authenticated users to the configured tenant automatically. Optional `AutoLinkRoleNames` are granted during that link.

Configuration example for a single-tenant deployment:

```json
{
  "IBeam": {
    "Identity": {
      "TenantProvisioning": {
        "Mode": "UseDefaultTenant",
        "DefaultTenantId": "225925cc-995e-4584-a63b-4f2cb4f38f6f",
        "AutoLinkUserToDefaultTenant": true,
        "AutoLinkRoleNames": [ "Member" ]
      }
    }
  }
}
```

Configuration example for strict membership-only auth:

```json
{
  "IBeam": {
    "Identity": {
      "TenantProvisioning": {
        "Mode": "RequireExistingTenant",
        "DefaultTenantId": "225925cc-995e-4584-a63b-4f2cb4f38f6f"
      }
    }
  }
}
```

## Code Samples

### SMS OTP first, email/password later

```csharp
var otp = await otpAuth.StartOtpAsync("16145551212", ct: ct);
var signedIn = await otpAuth.CompleteOtpAsync(
    otp.ChallengeId,
    codeFromSms,
    "16145551212",
    displayName: "Adam",
    ct);

var userIdClaim = signedIn.Token!.Claims.First(c => c.Type == "uid").Value;
Guid userId = Guid.Parse(userIdClaim);

await passwordAuth.StartEmailPasswordLinkAsync(
    userId,
    "adam@test.com",
    resetUrlBase: "https://app.example.com/finish-email-link",
    ct: ct);

await passwordAuth.CompleteEmailPasswordLinkAsync(
    userId,
    "adam@test.com",
    challengeId,
    verificationToken,
    "new secure password",
    ct);
```

### Email user adds SMS

```csharp
var challenge = await passwordAuth.StartPhoneLinkAsync(userId, "16145551212", ct);

await passwordAuth.CompletePhoneLinkAsync(
    userId,
    "16145551212",
    challenge.ChallengeId,
    codeFromSms,
    ct);
```

### Single-tenant OTP with configured tenant

With `Mode = UseDefaultTenant`, an omitted OTP tenant id resolves to `DefaultTenantId`. The service also stores the effective tenant id on the OTP challenge.

```csharp
var otp = await otpAuth.StartOtpAsync("+16142649686", ct: ct);

var result = await otpAuth.CompleteOtpAsync(
    otp.ChallengeId,
    codeFromSms,
    "16142649686",
    displayName: "Care Team User",
    ct);
```

If `AutoLinkUserToDefaultTenant` is `false` and the user is not already linked to `DefaultTenantId`, completion fails with an `IdentityValidationException`.

### Tenant invite onboarding

`TenantInviteService` handles tenant-preserving invitations without leaking whether the destination already belongs to an identity user. It sends an invite link, stores only a token hash, and on acceptance links the resolved user to the invite tenant.

```csharp
var invite = await tenantInvites.CreateInviteAsync(
    tenantId,
    new TenantInviteCreateRequest(
        DestinationType: TenantInviteDestinationTypes.Sms,
        PhoneNumber: "+16145551212",
        DisplayName: "Care Coordinator",
        RoleNames: ["Member"],
        SetAsDefaultTenant: true),
    invitedByUserId,
    ct);

var accepted = await tenantInvites.AcceptInviteAsync(
    new TenantInviteAcceptRequest(
        InviteToken: invite.InviteToken,
        Mode: TenantInviteAcceptModes.Otp,
        ChallengeId: challengeId,
        Code: codeFromSms),
    authenticatedUserId: null,
    ct);
```

Acceptance ensures tenant membership, applies role ids/names, calls `IIdentityUserExtensionCoordinator` with `Operation = "invite-accepted"`, applies optional `IResourceAccessService` grants when access-control services are registered, marks the invite redeemed, and returns a tenant token.

### Code-based options configuration

```csharp
builder.Services.AddIBeamIdentityServices(builder.Configuration);

builder.Services.Configure<TenantProvisioningOptions>(options =>
{
    options.Mode = TenantProvisioningMode.RequireExistingTenant;
    options.DefaultTenantId = Guid.Parse(builder.Configuration["Wellderly:TenantId"]!);
    options.AutoLinkUserToDefaultTenant = false;
});
```

## Access Control Examples

### Register modules, permissions, and mappings

```csharp
using IBeam.Identity.Models;
using IBeam.Identity.Services;

builder.Services.AddIBeamIdentityServices(builder.Configuration);

builder.Services.AddIBeamAccessControl(options =>
{
    options.OwnerRoleNames.Clear();
    options.OwnerRoleNames.Add("Owner");

    options.AdminRoleNames.Clear();
    options.AdminRoleNames.Add("Administrator");
    options.AdminRoleNames.Add("Admin");

    options.TenantUserManagementPermissionNames.Clear();
    options.TenantUserManagementPermissionNames.Add("identity.tenantusers.manage");
    options.TenantUserManagementPermissionNames.Add("identity.tenantinvites.manage");

    options.Modules.Add(new AccessModuleDefinition(
        Key: "work",
        Label: "Work",
        Description: "Work board access.",
        SupportedAccessLevels: ["view", "edit", "manage"],
        ImpliedByRoleNames: ["Owner", "Administrator", "Work Viewer"],
        ImpliedByPermissionNames: ["work.view"]));

    options.Modules.Add(new AccessModuleDefinition(
        Key: "products",
        Label: "Products",
        Description: "Product catalog access.",
        SupportedAccessLevels: ["view", "edit", "manage"],
        ImpliedByRoleNames: ["Owner", "Administrator", "Product Manager"],
        ImpliedByPermissionNames: ["products.view", "products.edit"]));
});

builder.Services.AddIBeamIdentityPermissionCatalog(catalog =>
{
    catalog.AddPermission(
        "work.view",
        resource: "Work",
        description: "Open the work board.",
        label: "View work",
        category: "Work",
        moduleKey: "work",
        accessLevel: "view");

    catalog.AddPermission(
        "products.edit",
        resource: "Products",
        description: "Edit product records.",
        label: "Edit products",
        category: "Products",
        moduleKey: "products",
        accessLevel: "edit");
});

builder.Services.AddIBeamIdentityPermissionMappings(mappings =>
{
    mappings.AllowRolesForPermission("work.view", "Owner", "Administrator", "Work Viewer");
    mappings.AllowRolesForPermission("products.edit", "Owner", "Administrator", "Product Manager");
});
```

### Evaluate access imperatively

```csharp
public sealed class WorkBoardService
{
    private readonly IIBeamAccessControlService _access;

    public WorkBoardService(IIBeamAccessControlService access)
    {
        _access = access;
    }

    public async Task<IReadOnlyList<WorkItemDto>> ListAsync(
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        await _access.RequireModuleAccessAsync(
            user,
            moduleKey: "work",
            minimumAccessLevel: "view",
            ct);

        return await LoadWorkItemsAsync(ct);
    }
}
```

### Build the current-user access context

```csharp
public sealed class AccessBootstrapService
{
    private readonly IIBeamAccessControlService _access;

    public AccessBootstrapService(IIBeamAccessControlService access)
    {
        _access = access;
    }

    public Task<AccessContextDto> GetBootContextAsync(
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        return _access.GetCurrentAccessContextAsync(user, tenantId: null, ct);
    }
}
```

The returned `AccessContextDto` contains role names, role IDs, resolved permissions, module access, resource grants, and convenience capabilities such as `CanManageUsers`, `CanManageRoles`, `CanManageAccess`, and `CanAssignOwner`.

## Service Operations, Auditing, And Permissions

Identity service methods that mutate authentication, tenants, roles, API credentials, grants, or sessions should remain service-bound operations. New custom service methods should be tagged with `[IBeamOperation("identity.<area>.<action>")]` or a more specific package operation name, and should route through `IServiceOperationExecutor` when policy/audit behavior is required.

Keep authorization-critical role assignment stable by preferring role IDs where available, while retaining role names for display, claims, and compatibility.

## Extended Docs And Agent Guidance

- AI prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Root implementation guide: [`../.agent/implementation-guide.md`](../.agent/implementation-guide.md)
- Azure Table schema inventory: [`../docs/identity-azure-table-schema-inventory.md`](../docs/identity-azure-table-schema-inventory.md)
- Roles, permissions, and grants: [`../docs/roles-permissions-and-grants.md`](../docs/roles-permissions-and-grants.md)
- Service logging and audit: [`../docs/service-logging-and-audit.md`](../docs/service-logging-and-audit.md)
- Service operation permissions: [`../docs/service-operation-permissions.md`](../docs/service-operation-permissions.md)
- Consuming API migration prompt: [`../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md`](../IBeam.AI.Enablement/examples/consuming-api-migration-prompt.md)

Agents should keep auth orchestration in services and avoid putting tenant, role, OTP, or API credential rules into controllers or repositories.
