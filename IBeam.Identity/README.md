# IBeam.Identity

`IBeam.Identity` is the contract package for the IBeam identity domain.

## Narrative Introduction

This package provides the shared language for identity workflows across API, services, and repository implementations. It contains interfaces, request/response models, options, and event contracts so higher-level packages can evolve independently behind stable abstractions.

## Identity Architecture (Simple View)

IBeam identity is intentionally layered:

1. API Layer (`IBeam.Identity.Api`)
- HTTP endpoints, auth middleware, request validation, response mapping.
- Controllers include OTP/password/OAuth/token/session/tenant-role APIs.
- Controllers are intentionally thin. They translate HTTP input/output and let expected service exceptions return friendly messages.

2. Contract Layer (`IBeam.Identity`)
- Interfaces, models, options, events, authorization attributes.
- No storage or provider-specific logic.

3. Service Layer (`IBeam.Identity.Services`)
- Core auth orchestration: OTP, password, OAuth, token issuing, tenant selection.
- Uses only contracts (`IIdentityUserStore`, `IOtpChallengeStore`, etc.).
- Owns business rules, validation decisions, lifecycle logging/events, and error classification.

4. Repository Layer (provider implementations)
- Azure Table provider (`IBeam.Identity.Repositories.AzureTable`) currently ships complete implementations.
- Entity Framework provider (`IBeam.Identity.Repositories.EntityFramework`) exists for EF-based identity paths.

5. Communications Layer
- Email/SMS abstractions and providers used by OTP/registration flows.

## Service-Owned Rules and Error Handling

IBeam keeps business behavior in services, not controllers or repositories. Services are responsible for enforcing auth and tenant rules, deciding whether an error is expected, emitting lifecycle events/logging, and throwing typed identity exceptions with user-safe messages when a request cannot proceed.

Controllers should stay as transport adapters. They validate basic HTTP shape, call the service, and map expected errors such as validation failures, missing membership, invalid credentials, and not-found cases into friendly API responses.

Unexpected exceptions and system failures should not be turned into detailed user-facing messages. They bubble to the API exception pipeline, which writes operational details through `IApiErrorSink`. With the Azure Table provider, those records are stored in the `SystemErrors` table. The response remains generic unless detailed errors are explicitly enabled.

## Auth Pattern Flexibility

IBeam treats a user as a stable `UserId` with one or more verified authentication identifiers bound to it. The user can start with SMS OTP, add email OTP later, set an email/password credential after that, and still remain the same identity.

The advantage for product teams is that onboarding can be low-friction without painting the account model into a corner:

- SMS-first onboarding for mobile or care-team workflows.
- Email OTP for magic-link or code-based sign-in.
- Email/password for users who need a traditional credential.
- 2FA using either verified email or verified SMS.
- OAuth links that point back to the same `UserId`.

Provider implementations should resolve auth identifiers through an indexed lookup, then load the canonical user by `UserId`. They should not scan the full user table for login.

## Features and Components

- auth service contracts:
  - `IIdentityAuthService`
  - `IIdentityOtpAuthService`
  - `IIdentityOAuthAuthService`
  - `ITokenService`
- store contracts:
  - `IIdentityUserStore`, `IOtpChallengeStore`, `IExternalLoginStore`
  - `ITenantMembershipStore`, `ITenantProvisioningService`, `IAuthSessionStore`
  - `ITenantRoleStore` for tenant-scoped role CRUD and assignment
  - `IPermissionAccessStore` for tenant permission-to-role mappings
- service contracts:
  - `ITenantRoleService`
  - `IRoleAccessAuthorizer`
  - `IPermissionAccessAuthorizer`
  - `IPermissionCatalogProvider`
- options models (`JwtOptions`, `OtpOptions`, `OAuthOptions`, `FeatureOptions`, etc.)
- lifecycle event contracts and default no-op implementations
- role access attributes (service-safe, no MVC dependency):
  - `[RoleAccess("owner", "billing")]`
  - `[RoleAccessId("3f7a4b4f-8fc5-49bb-b6fe-1f4a9b43a3e9")]`
  - `[AllowAllRoleAccess]`
- dynamic permission attributes:
  - `[PermissionAccess("SavePatient")]`
  - `[PermissionAccessId("6c76f166-b130-4c80-bf7e-99d38ea1a75f")]`

## Models vs Entities

1. Models
- Defined in `IBeam.Identity.Models`.
- Used by API and services (requests/responses/domain contracts).
- Examples: `AuthResultResponse`, `RegisterUserRequest`, `TenantInfo`.

2. Entities
- Provider-specific persistence shapes (e.g., Azure Table entities).
- Include storage keys (`PartitionKey`, `RowKey`) and persistence metadata.
- Examples in Azure Table provider: `TenantEntity`, `UserTenantEntity`, `OtpChallengeEntity`.

## Azure Table Schema (Current Provider)

### ElCamino identity tables
- `AspNetUsers`: base user identities.
- `AspNetRoles`: role definitions.
- `AspNetIndex`: identity lookup/index support.

### IBeam custom identity tables
- `Tenants`: tenant master records.
- `TenantUsers`: tenant-to-user membership index.
- `UserTenants`: user-to-tenant membership index.
- `TenantRoles`: tenant-scoped roles.
- `PermissionRoleMaps`: tenant permission mapping to role names/ids.
- `OtpChallenges`: OTP lifecycle records (destination, hash, attempts, expiry, consume state).
- `AuthIdentifiers`: auth lookup bindings from email/SMS identifiers to canonical user ids.
- `ExternalLogins`: OAuth provider-user links.
- `AuthSessions`: refresh/session tracking and revocation.
- `SystemLogs`: operational log sink records.
- `SystemErrors`: operational API error sink records.
- `Schema`: schema version marker for bootstrap.

## Table Naming and Prefixing

For Azure Table identity provider, physical table name is:

`{TablePrefix}{BaseTableName}`

Examples:
- `TablePrefix = "IBeam"` + `AspNetUsers` => `IBeamAspNetUsers`
- `TablePrefix = "Acme"` + `TenantUsers` => `AcmeTenantUsers`

This applies to both ElCamino and custom IBeam identity tables.

If `IBeam:Identity:AzureTable:TablePrefix` is unset, the provider uses an empty prefix. IBeam does not derive a prefix from the environment name, application name, or connection string. Environment-specific names such as `WellderlyTest` must be configured explicitly as `TablePrefix`.

Operational tables follow the same rule:

- empty prefix: `SystemLogs`, `SystemErrors`, `Schema`
- `TablePrefix = "Acme"`: `AcmeSystemLogs`, `AcmeSystemErrors`, `AcmeSchema`

`AcmeIdentitySystemLogs` and `AcmeIdentitySystemErrors` are not default IBeam table names unless the app explicitly overrides `SystemLogsTableName` or `SystemErrorsTableName`.

The generic repository provider (`IBeam.Repositories.AzureTables`) uses `IBeam:Repositories:AzureTables:TableNamePrefix`. When that value is unset, repository tables are also unprefixed apart from Azure-safe normalization of the entity table name. This setting is separate from `IBeam:Identity:AzureTable:TablePrefix`.

## Connection String Resolution Cascade

### Current implemented behavior

Azure Table providers currently resolve connection strings with fallback precedence.

1. Identity AzureTable provider (`IBeam.Identity.Repositories.AzureTable`)
- 1) `IBeam:Identity:AzureTable:StorageConnectionString`
- 2) `IBeam:AzureTables`
- 3) `IBeam:Repositories:ConnectionString`
- 4) `IBeam:ConnectionString`
- 5) `ConnectionStrings:AzureTables`
- 6) `ConnectionStrings:AzureStorage`
- 7) `ConnectionStrings:IBeam`
- 8) `ConnectionStrings:DefaultConnection`
- 9) `ConnectionStrings:IdentityAzureTable`

2. Generic AzureTables repository provider (`IBeam.Repositories.AzureTables`)
- 1) `IBeam:Repositories:AzureTables:ConnectionString`
- 2) `IBeam:AzureTables`
- 3) `IBeam:Repositories:ConnectionString`
- 4) `IBeam:ConnectionString`
- 5) `ConnectionStrings:AzureTables`
- 6) `ConnectionStrings:AzureStorage`
- 7) `ConnectionStrings:IBeam`
- 8) `ConnectionStrings:DefaultConnection`

3. Identity EntityFramework provider (`IBeam.Identity.Repositories.EntityFramework`)
- 1) `{configSectionPath}:ConnectionString` (default `IdentityEf`)
- 2) `IBeam:Identity:EntityFramework:ConnectionString`
- 3) `IBeam:Repositories:EntityFramework:ConnectionString`
- 4) `IBeam:Repositories:ConnectionString`
- 5) `IBeam:ConnectionString`
- 6) `ConnectionStrings:IdentityEf`
- 7) `ConnectionStrings:IdentityEntityFramework`
- 8) `ConnectionStrings:IBeam`
- 9) `ConnectionStrings:DefaultConnection`

## Configuration Models Exposed

- `IBeam:Identity:Jwt`
- `IBeam:Identity:Otp`
- `IBeam:Identity:OAuth`
- `IBeam:Identity:Features`
- `IBeam:Identity:Events`
- `IBeam:Identity:TenantProvisioning`
- `IBeam:Identity:EmailTemplates`
- `IBeam:Identity:PermissionAccess`
- `IBeam:Identity:RoleManagement`

## Tenant Provisioning Policy

Auth flows use `IBeam:Identity:TenantProvisioning` to decide what happens after a user is authenticated but no active tenant membership is available.

Configuration properties:

- `Mode`: `AutoCreateTenantForNewUser`, `RequireExistingTenant`, or `UseDefaultTenant`.
- `DefaultTenantId`: tenant ID used by `UseDefaultTenant`, and optionally by `RequireExistingTenant`.
- `AutoLinkUserToDefaultTenant`: when `true`, `UseDefaultTenant` links authenticated users to `DefaultTenantId` if no membership exists.
- `AutoLinkRoleNames`: optional role names to ensure/grant during default-tenant auto-link.

Default mode is `AutoCreateTenantForNewUser`, which preserves the original workspace-per-user behavior:

```json
{
  "IBeam": {
    "Identity": {
      "TenantProvisioning": {
        "Mode": "AutoCreateTenantForNewUser"
      }
    }
  }
}
```

## Tenant Extension Pattern

IBeam owns `IdentityTenant` for identity/auth concerns, while an application can own its app/domain tenant entity such as `Tenant`, `Organization`, `Workspace`, `Practice`, or `Business`.

`IdentityTenant` stays minimal:

- `TenantId`
- `Name`
- `NormalizedName`
- `Status`
- `CreatedAt`
- `UpdatedAt`

Applications map their extended tenant row by the same `TenantId` and keep app/business fields there. For example, Hubbsly can keep `Slug`, `DisplayName`, `StripeAppKey`, `IsActive`, `IsDeleted`, `CreatedUtc`, and `UpdatedUtc` in `Hubbsly.Tenants` while IBeam keeps auth tenant metadata in `IBeamIdentityTenants`.

Core extension contracts:

- `IIdentityTenantExtension`
- `ITenantExtensionStore<TTenant>`
- `ITenantExtensionResolver<TTenant>`
- `ITenantExtensionCoordinator`
- `ITenantLifecycleHook`
- `ITenantMetadataProvider`
- `IIdentityTenantStore`
- `IIdentityTenantService`

Service registration:

```csharp
services.AddIBeamIdentityServices(configuration);
services.AddIBeamIdentityTenantExtension<Tenant, TenantExtensionStore>();
services.AddIBeamIdentityTenantMetadataProvider<TenantMetadataProvider>();
```

When configured, IBeam hydrates the app-owned tenant extension during tenant creation, tenant selection, tenant listing, and tenant membership bootstrap. If the identity tenant exists but the app tenant row does not, the app's `ITenantExtensionStore<TTenant>.CreateAsync` is called. If the app row exists, `UpdateFromIdentityTenantAsync` can keep display metadata in sync.

`ITenantMetadataProvider` lets an app project app-owned metadata back into IBeam tenant displays. For example, a Hubbsly provider can return `DisplayName = Tenant.DisplayName` and `IsActive = Tenant.IsActive && !Tenant.IsDeleted`; IBeam then uses that metadata when returning tenant selections and before issuing tenant-scoped tokens.

### User extensions

IBeam owns identity/security primitives only: identity user id, login identifiers, verification/auth state, passwords, OTP, sessions, refresh tokens, tenant membership, and role/token claims. Applications own extended user profile data such as display name, first and last name, preferences, onboarding state, and tenant-scoped profile metadata.

Register a host-owned user extension store when the app wants IBeam lifecycle events to project identity users into its own user table:

```csharp
services.AddIBeamIdentityServices(configuration);
services.AddIBeamIdentityUserExtension<User, UserExtensionStore>();
```

Core contracts:

- `IIdentityUserExtension`
- `IIdentityUserProfileExtension`
- `IIdentityUserExtensionStore<TUserExtension>`
- `IIdentityUserExtensionResolver<TUserExtension>`
- `IIdentityUserExtensionCoordinator`
- `UserExtensionContext`

When configured, IBeam invokes the user extension store during user creation and when auth resolves to a tenant-scoped login or tenant selection. If the app row does not exist, `CreateAsync` is called with the `IdentityUser` and `UserExtensionContext`; if it already exists, `UpdateFromIdentityUserAsync` can sync identity-owned values such as normalized email or phone. If no user extension store is registered, IBeam continues auth normally and does not create user profile rows.

IBeam does not expose built-in profile extension routes and does not persist app-specific user profile fields. For Hubbsly-style apps, the app-owned `Users` table should be keyed by selected IBeam `TenantId` plus IBeam `UserId`.

For single-tenant deployments, use `UseDefaultTenant` with an explicit tenant ID. Auth requests that omit tenant ID use this configured default; IBeam does not infer it from environment name or storage account.

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

Use `RequireExistingTenant` to disable tenant creation and automatic linking from auth flows. If the user is not already linked to the requested/configured tenant, auth fails with a validation error instead of creating `Tenants`, `TenantUsers`, `UserTenants`, or `TenantRoles` rows.

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

Equivalent code configuration:

```csharp
builder.Services.Configure<TenantProvisioningOptions>(options =>
{
    options.Mode = TenantProvisioningMode.UseDefaultTenant;
    options.DefaultTenantId = Guid.Parse("225925cc-995e-4584-a63b-4f2cb4f38f6f");
    options.AutoLinkUserToDefaultTenant = true;
    options.AutoLinkRoleNames.Add("Member");
});
```

## Examples

### 1) API composition in a host app

```csharp
builder.Services.AddIBeamIdentityApi(builder.Configuration);
builder.Services.AddIBeamIdentityApiControllers();
```

### 2) Azure Table identity configuration with prefix and scoped connection

```json
{
  "IBeam": {
    "Identity": {
      "AzureTable": {
        "StorageConnectionString": "UseDevelopmentStorage=true",
        "TablePrefix": "Acme",
        "UserTableName": "AspNetUsers",
        "RoleTableName": "AspNetRoles",
        "IndexTableName": "AspNetIndex"
      }
    }
  }
}
```

### 3) Fallback-only configuration (top-level IBeam connection)

```json
{
  "IBeam": {
    "ConnectionString": "UseDevelopmentStorage=true"
  }
}
```

With AzureTable providers, this can be used when deeper scoped keys are not supplied.

### 4) Service role access example

```csharp
[RoleAccess("SavePatient")]
public sealed class PatientService
{
    private readonly IRoleAccessAuthorizer _roleAccess;

    public PatientService(IRoleAccessAuthorizer roleAccess)
    {
        _roleAccess = roleAccess;
    }

    public Task SavePatientAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        _roleAccess.EnsureAuthorizedForCurrentMethod(user, this);
        return Task.CompletedTask;
    }
}
```

### 5) Auth identifier contract example

```csharp
IdentityUser? byEmail = await userStore.FindByEmailAsync("adam@test.com", ct);
IdentityUser? bySms = await userStore.FindByPhoneAsync("16145551212", ct);

// Both should return the same UserId after the identifiers are linked.
```

### 6) Add another auth pattern to the current user

```csharp
await auth.StartEmailPasswordLinkAsync(
    userId,
    "adam@test.com",
    resetUrlBase: "https://app.example.com/finish-email-link",
    ct: ct);

await auth.CompleteEmailPasswordLinkAsync(
    userId,
    "adam@test.com",
    challengeId,
    verificationToken,
    "new secure password",
    ct);
```

### 7) Bootstrap a tenant membership and roles

```csharp
await tenantRoles.EnsureTenantMembershipAndGrantRolesAsync(
    new TenantMembershipRoleBootstrapRequest(
        TenantId: configuredTenantId,
        UserId: userId,
        TenantName: "Wellderly",
        RoleNames: new[] { "Member" },
        SetAsDefault: true),
    ct);
```
