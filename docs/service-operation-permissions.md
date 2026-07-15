# IBeam Service Operation Permissions

IBeam service operation permissions let teams secure service calls by stable operation names such as `pricing.update`, `purchases.delete`, or `patients.discharge`.

The feature is intentionally split into two parts:

| Layer | Purpose | Required? |
|---|---|---|
| Runtime authorization | Checks whether a principal can call a service operation. | Optional, enabled by configuration. |
| Permission management | Adds, updates, disables, or deletes operation permission rules. | Optional. |
| API endpoints | Exposes permission management over HTTP. | Optional. |

This allows three adoption modes:

1. Config-only permissions.
2. DB/store-backed permissions managed by scripts.
3. DB/store-backed permissions managed by API/UI.

## Operation Names

Operation names should follow the same naming pattern as audit actions:

```text
pricing.create
pricing.update
pricing.delete
purchases.archive
patients.discharge
transactions.export
```

For custom methods, use `IBeamOperationAttribute`:

```csharp
[IBeamOperation("patients.discharge")]
public Task DischargeAsync(Guid patientId, CancellationToken ct = default)
{
    // domain logic
}
```

The same operation name can be used for:

- audit action
- permission check
- configuration rule
- DB rule
- API/UI display

## Runtime Authorization

Register runtime authorization:

```csharp
builder.Services.AddIBeamAccessControlServices(builder.Configuration);
```

Or register only service-operation authorization:

```csharp
builder.Services.AddIBeamServiceOperationAuthorization(builder.Configuration);
```

Use the optional Azure Table store when rules should be persisted:

```csharp
builder.Services.AddIBeamAccessControlServices(builder.Configuration);
builder.Services.AddIBeamAccessControlAzureTableStores(builder.Configuration);
```

```json
{
  "IBeam": {
    "AccessControl": {
      "AzureTable": {
        "StorageConnectionString": "<connection-string>",
        "TablePrefix": "IBeam",
        "ServiceOperationPermissionsTableName": "ServiceOperationPermissions"
      }
    }
  }
}
```

With the default `IBeam` prefix, the physical table is `IBeamServiceOperationPermissions`.

Configuration:

```json
{
  "IBeam": {
    "Services": {
      "Authorization": {
        "Enabled": true,
        "DefaultMode": "require-permission"
      }
    }
  }
}
```

When disabled, service-operation authorization allows calls for backward compatibility.

When enabled with `require-permission`, a call is denied unless a matching allow rule or permission map exists.

Base services can enforce operation permissions automatically when derived services pass
`IServiceOperationAuthorizer` and `IServiceOperationPrincipalProvider` into the base constructor.

```csharp
public sealed class PricingService : BaseServiceAsync<PriceEntity, PriceModel>
{
    public PricingService(
        IBaseRepositoryAsync<PriceEntity> repository,
        IModelMapper<PriceEntity, PriceModel> mapper,
        IServiceOperationAuthorizer operationAuthorizer,
        IServiceOperationPrincipalProvider principalProvider)
        : base(
            repository,
            mapper,
            serviceOperationAuthorizer: operationAuthorizer,
            serviceOperationPrincipalProvider: principalProvider)
    {
        AllowSave = true;
        AllowDelete = true;
        AllowArchive = true;
    }
}
```

With `IBeam:Services:Authorization:Enabled = true`, the base service checks:

```text
pricing.create
pricing.update
pricing.delete
pricing.archive
pricing.unarchive
```

`AddIBeamHttpContextAuditActor()` also registers an HTTP-context principal provider, so API apps can use the current request user for service authorization.

## Rule Shape

A service operation permission rule has:

| Field | Purpose |
|---|---|
| `Pattern` | Exact operation or wildcard such as `pricing.*`. |
| `Effect` | `allow` or `deny`. |
| `SubjectTypes` | Optional subject filter such as `user`, `agent`, or `api-credential`. Empty means any subject type. |
| `RoleNames` | Role names that match the rule. Empty means any role. |
| `RoleIds` | Stable role ids that match the rule. |
| `Priority` | Tie-breaker inside the same source/specificity. |
| `Status` | `active` or `disabled`. |

Resolution rules:

1. Configuration emergency overrides.
2. Configuration rules.
3. Store/DB rules.
4. Permission role map fallback.
5. Default mode.

Inside the same source:

1. Exact match beats wildcard.
2. More specific wildcard beats broader wildcard.
3. Higher priority wins.
4. Deny wins ties.

## Accounting Example

Goal:

```text
Accounting can access:
- all pricing service calls
- purchases.delete
- purchases.archive
- all transactions calls

Accounting cannot access:
- all sales calls
- coupons.delete
```

Configuration:

```json
{
  "IBeam": {
    "Services": {
      "Authorization": {
        "Enabled": true,
        "DefaultMode": "require-permission",
        "Rules": [
          {
            "Pattern": "pricing.*",
            "Effect": "allow",
            "SubjectTypes": [ "user" ],
            "RoleNames": [ "Accounting" ]
          },
          {
            "Pattern": "purchases.delete",
            "Effect": "allow",
            "SubjectTypes": [ "user" ],
            "RoleNames": [ "Accounting" ]
          },
          {
            "Pattern": "purchases.archive",
            "Effect": "allow",
            "SubjectTypes": [ "user" ],
            "RoleNames": [ "Accounting" ]
          },
          {
            "Pattern": "transactions.*",
            "Effect": "allow",
            "SubjectTypes": [ "user" ],
            "RoleNames": [ "Accounting" ]
          },
          {
            "Pattern": "sales.*",
            "Effect": "deny",
            "SubjectTypes": [ "user" ],
            "RoleNames": [ "Accounting" ]
          },
          {
            "Pattern": "coupons.delete",
            "Effect": "deny",
            "SubjectTypes": [ "user" ],
            "RoleNames": [ "Accounting" ]
          }
        ]
      }
    }
  }
}
```

## Dynamic Management Service

Register the management service only when the app wants code/API/UI to edit rules:

```csharp
builder.Services.AddIBeamServiceOperationPermissionManagement();
```

Create or update a rule:

```csharp
await permissionService.UpsertRuleAsync(
    tenantId,
    new UpsertServiceOperationPermissionRequest
    {
        Pattern = "pricing.*",
        Effect = ServiceOperationPermissionEffects.Allow,
        SubjectTypes = [AccessSubjectTypes.User],
        RoleNames = ["Accounting"]
    },
    updatedByUserId,
    ct);
```

Disable a rule without deleting history:

```csharp
await permissionService.DisableRuleAsync(
    tenantId,
    ruleId,
    updatedByUserId,
    ct);
```

Delete a rule:

```csharp
await permissionService.DeleteRuleAsync(
    tenantId,
    ruleId,
    ct);
```

Locked-down teams can skip this service and seed `IServiceOperationPermissionStore` directly from scripts.

For Azure Table scripting, seed rows through `AzureTableServiceOperationPermissionStore` or write rows to:

```text
Table:        IBeamServiceOperationPermissions
PartitionKey: TEN|{tenantId}
RowKey:       SOP|{ruleId}
```

## Optional API

The API surface is only present when the app maps `IBeam.AccessControl.Api` endpoints.

```csharp
app.MapIBeamAccessControl("/api", authorizationPolicy: "AccessControlAdmin");
```

Management endpoints:

```http
GET    /api/tenants/{tenantId}/access-control/service-permissions
POST   /api/tenants/{tenantId}/access-control/service-permissions
PUT    /api/tenants/{tenantId}/access-control/service-permissions/{ruleId}
POST   /api/tenants/{tenantId}/access-control/service-permissions/{ruleId}/disable
DELETE /api/tenants/{tenantId}/access-control/service-permissions/{ruleId}
POST   /api/tenants/{tenantId}/access-control/service-permissions/check
```

Example API request:

```http
POST /api/tenants/{tenantId}/access-control/service-permissions
```

```json
{
  "pattern": "pricing.*",
  "effect": "allow",
  "subjectTypes": [ "user" ],
  "roleNames": [ "Accounting" ],
  "roleIds": [],
  "priority": 0
}
```

Check an operation:

```http
POST /api/tenants/{tenantId}/access-control/service-permissions/check
```

```json
{
  "operationName": "pricing.update"
}
```

## End-to-End Workflow

With IBeam Identity:

1. Create the role `Accounting`.
2. Add service operation permission rules for `Accounting`.
3. Assign `Accounting` to a user.
4. User logs in and receives `role` / `rid` claims.
5. Service operation authorization evaluates those claims.

With custom auth:

1. Create/manage the role in the custom auth system.
2. Add service operation permission rules in IBeam.
3. Ensure the token emits compatible claims:

```csharp
new Claim("tid", tenantId.ToString("D"));
new Claim("sub", userId.ToString("D"));
new Claim("role", "Accounting");
new Claim("rid", accountingRoleId.ToString("D"));
```

IBeam AccessControl evaluates those claims without requiring IBeam Identity.

## Agent Permissions

Agents should not automatically inherit user permissions unless the app chooses that behavior.

Use subject types:

```json
{
  "Pattern": "transactions.*",
  "Effect": "allow",
  "SubjectTypes": [ "user" ],
  "RoleNames": [ "Accounting" ]
}
```

```json
{
  "Pattern": "transactions.read",
  "Effect": "allow",
  "SubjectTypes": [ "agent" ],
  "RoleNames": [ "AccountingAgent" ]
}
```

Then:

```text
Accounting user -> transactions.export allowed
Accounting agent -> transactions.export denied
AccountingAgent agent -> transactions.read allowed
```

## Emergency Override

Configuration can be used as a fast recovery mechanism.

Example: revoke non-admin delete access to referral codes immediately:

```json
{
  "IBeam": {
    "Services": {
      "Authorization": {
        "EmergencyOverrides": [
          {
            "Pattern": "referralcodes.delete",
            "Effect": "deny",
            "RoleNames": [ "Accounting", "Member", "Support" ]
          }
        ]
      }
    }
  }
}
```

This lets teams recover from bad DB/store rules without code changes.
