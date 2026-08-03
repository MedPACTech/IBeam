# OAuth for IBeam MCP: Operator Guide

This guide covers deploying an IBeam MCP endpoint that accepts existing IBeam API keys and OAuth 2.0 access tokens issued by IBeam Identity.

## Runtime Architecture

```text
OAuth-only consumer
  -> MCP protected-resource discovery
  -> IBeam Identity authorization-server discovery
  -> Authorization Code + PKCE and consent, or client_credentials
  -> resource-bound access token
  -> MCP endpoint (IBeamMcp policy)
       -> API-key authentication OR OAuth JWT authentication
       -> required MCP scope
       -> existing tool role/access-control checks
       -> service operation and audit
```

IBeam Identity owns clients, consent, authorization codes, refresh sessions, token issuance, revocation, signing keys, and the live client-status check. `IBeam.Ai.Api` owns protected-resource metadata, MCP challenge headers, endpoint mapping, and tool context.

## Host Configuration

Use the same exact canonical resource URI in the Identity client registration and MCP host. A path resource such as `https://api.example.com/api/mcp` is intentionally different from the origin `https://api.example.com`.

```json
{
  "IBeam": {
    "Identity": {
      "Jwt": {
        "Issuer": "https://identity.example.com",
        "Audience": "identity-api",
        "SigningMode": "asymmetric",
        "KeyId": "identity-2026-08",
        "PrivateKeyPem": "<load-from-secret-store>",
        "AccessTokenMinutes": 15,
        "RefreshTokenDays": 30,
        "ClockSkewSeconds": 60
      },
      "OAuthServer": {
        "Enabled": true,
        "Issuer": "https://identity.example.com",
        "ClientIdMetadataDocumentsEnabled": true,
        "DynamicClientRegistrationEnabled": false
      }
    }
  }
}
```

Do not store private keys, client secrets, refresh tokens, or access tokens in committed configuration. Bind them from the deployment secret store.

Register the MCP resource and policy after the normal Identity services:

```csharp
using IBeam.Ai;
using IBeam.Identity.Api.DependencyInjection;

builder.Services.AddIBeamIdentityApi(builder.Configuration);

builder.Services.AddIBeamAiMcp(
    configureTools: tools =>
    {
        // Register host tools and their required scopes here.
    },
    configureOAuth: oauth =>
    {
        oauth.Enabled = true;
        oauth.ResourceUri = "https://api.example.com/api/mcp";
        oauth.AuthorizationServerUri = "https://identity.example.com";
        oauth.ResourceName = "Example MCP";
        oauth.RequiredScope = "tool:mcp";
        oauth.SupportedScopes = [ "tool:mcp" ];
    });

builder.Services.AddIBeamMcpAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapIBeamMcp(
    "/api/mcp",
    IBeamMcpAuthenticationDefaults.AuthorizationPolicy);
```

Confirm these public endpoints through the deployment ingress:

| Endpoint | Purpose |
|---|---|
| `/.well-known/oauth-protected-resource/api/mcp` | Canonical MCP resource metadata |
| `/.well-known/oauth-protected-resource` | Compatibility resource metadata |
| `/.well-known/oauth-authorization-server` | Identity OAuth server metadata |
| `/.well-known/jwks.json` | Active and transition JWT public keys |
| `/oauth/authorize` | User authorization and consent |
| `/oauth/token` | Code, refresh, and client-credentials exchange |
| `/oauth/revoke` | Refresh-session and optional consent revocation |

## Client Registration

Create and manage tenant clients through `api/tenants/{tenantId}/oauth-clients`. The caller must be an authorized human administrator for that tenant. Prefer this API over putting client secret hashes in configuration.

For a desktop, CLI, or other public consumer:

```json
{
  "displayName": "OAuth-only MCP Consumer",
  "clientType": "public",
  "redirectUris": [ "http://127.0.0.1:43119/oauth/callback" ],
  "allowedGrantTypes": [ "authorization_code", "refresh_token" ],
  "allowedScopes": [ "tool:mcp" ],
  "allowedResources": [ "https://api.example.com/api/mcp" ],
  "requirePkce": true
}
```

Public clients never receive or store a client secret. They must generate a fresh high-entropy verifier, send its S256 challenge to `/oauth/authorize`, validate `state` exactly, and send the verifier only to `/oauth/token`.

For a server-side machine client:

```json
{
  "displayName": "Nightly MCP Agent",
  "clientType": "confidential",
  "allowedGrantTypes": [ "client_credentials" ],
  "allowedScopes": [ "tool:mcp" ],
  "allowedResources": [ "https://api.example.com/api/mcp" ],
  "requirePkce": false
}
```

The create response returns the confidential secret once. Put it directly into the consuming application's secret store. A machine client is tenant-scoped and does not have a username or password.

## Consent and Permissions

OAuth never bypasses IBeam roles or access controls. The issued permissions are the intersection of:

1. Scopes requested by the consumer.
2. Scopes allowed for the OAuth client.
3. Tenant scope policy.
4. The signed-in subject's roles and access controls.
5. Scopes approved by consent.

The MCP endpoint first requires `RequiredScope`. Individual tools then continue to use `DefaultAgentToolAccessPolicy` and service-layer authorization. Keep broad permissions out of `SupportedScopes`; that list is public metadata, not a grant.

## Key and Secret Rotation

For JWT signing-key rotation:

1. Generate a new RSA key of at least 2048 bits and a unique key ID.
2. Make it the active private key.
3. Add the old public key to `PreviousSigningKeys` with `PublishUntilUtc` later than the longest remaining access-token lifetime plus clock skew.
4. Deploy Identity and verify both key IDs in JWKS.
5. Remove the old key after its publication window expires.

For confidential clients, call `POST api/tenants/{tenantId}/oauth-clients/{clientId}/rotate-secret`, update the consumer secret store immediately, and retire the old deployment value. Disable a client for a reversible stop; revoke it for permanent retirement. MCP OAuth authentication checks current client status on every request.

## API-Key Migration

API keys and OAuth can run side by side under the `IBeamMcp` policy.

1. Keep existing API-key roles and access controls unchanged.
2. Register an OAuth client with the equivalent least-privilege scopes.
3. Validate discovery, token acquisition, and a harmless MCP read operation.
4. Move the consumer to `Authorization: Bearer {access_token}`.
5. Monitor authorization failures and audit identity fields.
6. Revoke the old API credential only after the OAuth path is stable.

API-key requests retain `ApiCredentialId`. OAuth requests expose `OAuthClientId` and `OAuthSessionId`; they do not fabricate an API credential ID.

## Troubleshooting

| Symptom | Check |
|---|---|
| `401` with `resource_metadata` | Fetch that document, then use its advertised authorization server. |
| `401` after token exchange | Verify issuer, signature key ID, expiration, exact `aud`, `resource`, tenant, and active client status. |
| `403 insufficient_scope` | Request the advertised required scope and confirm the user/client/tenant/consent intersection grants it. |
| `invalid_redirect_uri` or `invalid_request` | Match the registered redirect URI byte for byte; do not add or remove a trailing slash. |
| `invalid_target` | Send the exact canonical MCP resource in both authorization and token requests. |
| `invalid_grant` on refresh | The token may be expired, revoked, or already rotated. Restart authorization instead of retrying the old token. |
| API key works but OAuth does not | Confirm `AddIBeamMcpAuthorization`, the `IBeamMcp` mapping policy, and Identity client-store access are registered in the MCP host. |
| OAuth works but a tool is hidden | The endpoint scope passed, but the principal lacks that tool's role/scope or service permission. |

Never log authorization codes, client secrets, access tokens, refresh tokens, raw API keys, or private signing keys. Log only safe correlation values such as tenant ID, OAuth client ID, session ID, JWT ID, API credential ID, route, outcome, and denial category.

## Security Checklist

- [ ] Identity issuer, MCP resource URI, redirect URIs, and allowed resources are exact and HTTPS outside loopback development.
- [ ] Public clients require Authorization Code + PKCE S256 and have no secret.
- [ ] Confidential secrets and JWT private keys come from a managed secret store.
- [ ] `client_credentials` clients are tenant-scoped and limited to required scopes.
- [ ] Dynamic client registration remains disabled unless a known consumer requires it and rate limits are configured.
- [ ] Consent and tenant policy grant only the minimum tool, permission, module, role, and API scopes.
- [ ] MCP JWT validation checks issuer, signature, lifetime, exact audience/resource, tenant, client status, and resource registration.
- [ ] Access and refresh tokens are excluded from logs, traces, URLs, analytics, and exception payloads.
- [ ] Refresh rotation, replay rejection, revocation, client disable, and signing-key rollover are tested in the deployment environment.
- [ ] Existing API-key consumers retain their challenge and permission behavior during migration.

## Compatibility Validation

`OAuthMcpEndToEndTests` is the repository's OAuth-only consuming application harness. It validates protected-resource discovery, public PKCE consent, permission intersection, tenant isolation, token exchange, MCP invocation, refresh rotation, replay rejection, revocation, API-key compatibility, and confidential `client_credentials`. Run it with:

```powershell
dotnet test IBeam.Tests.Identity.Api/IBeam.Tests.Identity.Api.csproj --filter OAuthMcpEndToEndTests
```
