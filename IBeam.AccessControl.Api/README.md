# IBeam.AccessControl.Api

Optional ASP.NET Core endpoint wiring for IBeam access control.

Register runtime services:

```csharp
builder.Services.AddIBeamAccessControlServices(builder.Configuration);
```

Register dynamic service-operation permission management only when the app should allow runtime edits:

```csharp
builder.Services.AddIBeamServiceOperationPermissionManagement();
```

Map endpoints:

```csharp
app.MapIBeamAccessControl("/api", authorizationPolicy: "AccessControlAdmin");
```

Service operation permission endpoints:

```http
GET    /api/tenants/{tenantId}/access-control/service-permissions
POST   /api/tenants/{tenantId}/access-control/service-permissions
PUT    /api/tenants/{tenantId}/access-control/service-permissions/{ruleId}
POST   /api/tenants/{tenantId}/access-control/service-permissions/{ruleId}/disable
DELETE /api/tenants/{tenantId}/access-control/service-permissions/{ruleId}
POST   /api/tenants/{tenantId}/access-control/service-permissions/check
```

ASP.NET Core endpoints for tenant-scoped dynamic resource access grants.
