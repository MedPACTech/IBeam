export const identitySnippets = {
  apiComposition: `builder.Services.AddIBeamIdentityApi(builder.Configuration);
builder.Services.AddIBeamIdentityApiControllers();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();`,

  registration: `POST /api/auth/start-email-password-registration
Content-Type: application/json

{
  "email": "developer@example.com",
  "displayName": "Demo Developer",
  "resetUrlBase": "http://localhost:5174/registration"
}`,

  otp: `POST /api/auth/startotp
Content-Type: application/json

{
  "destination": "16145551212",
  "tenantId": null
}`,

  passwordLogin: `POST /api/auth/password-login
Content-Type: application/json

{
  "email": "developer@example.com",
  "password": "correct horse battery staple"
}`,

  twoFactor: `POST /api/auth/2fa/setup/start
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "method": "email"
}`,

  oauth: `GET /api/auth/oauth/start?provider=google&redirectUri=http://localhost:5174/oauth

POST /api/auth/oauth/complete
Content-Type: application/json

{
  "provider": "google",
  "state": "{state}",
  "code": "{authorizationCode}",
  "redirectUri": "http://localhost:5174/oauth"
}`,

  tenantProvisioning: `"IBeam": {
  "Identity": {
    "TenantProvisioning": {
      "Mode": "AutoCreateTenantForNewUser"
    }
  }
}`,

  sessions: `POST /api/auth/refresh
Content-Type: application/json

{
  "refreshToken": "{refreshToken}"
}

POST /api/auth/sessions/revoke
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "sessionId": "{sessionId}"
}`,

  profileExtensions: `PUT /api/auth/profile/extensions
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "attributes": {
    "department": "Engineering",
    "demoTrack": "Identity"
  }
}`,

  roles: `POST /api/tenants/{tenantId}/roles
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "name": "admin"
}`,

  permissions: `PUT /api/tenants/{tenantId}/permissions/mappings/by-name
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "permissionName": "Identity.ManageRoles",
  "roleNames": ["owner", "admin"],
  "roleIds": []
}`,

  azureTables: `"IBeam": {
  "Identity": {
    "AzureTable": {
      "StorageConnectionString": "UseDevelopmentStorage=true",
      "TablePrefix": "Demo"
    }
  }
}`,

  troubleshooting: `Symptoms to check:
- 401: missing, expired, malformed, or wrong-audience access token
- 403: token is valid but role/permission policy is not satisfied
- Schema errors: Azurite not running or tables not initialized
- Tenant errors: provisioning mode requires a default or existing tenant`
} satisfies Record<string, string>;

export type SnippetKey = keyof typeof identitySnippets;
