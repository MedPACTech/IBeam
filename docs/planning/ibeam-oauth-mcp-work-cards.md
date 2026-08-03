# IBeam OAuth for MCP Work Cards

Source: https://github.com/MedPACTech/IBeam

Goal: allow remote MCP consumers to authenticate with OAuth while preserving the existing API-key path and the same tenant, role, scope, tool, and resource authorization behavior.

Architecture flow:

```text
MCP client
  -> protected-resource discovery on the MCP server
  -> authorization-server discovery on IBeam Identity
  -> authorization code + PKCE and tenant-aware consent
  -> resource-bound access token
  -> Bearer authentication at the MCP HTTP boundary
  -> normalized IBeam claims
  -> existing tool and resource authorization
```

Authorization rule:

```text
requested scopes
  intersect client-allowed scopes
  intersect subject tenant roles/access
  intersect tenant policy
  intersect granted consent
  = effective token scopes
```

API keys remain supported. OAuth clients, authorization codes, consents, and grants are separate security records from API credentials, but both authentication paths converge on the same authorization claims before MCP tool execution.

## Cards

### IBeam OAuth MCP 001: Add authorization-server contracts and client registry

Source key: `IBM-024`

Create the repository-independent OAuth authorization-server model without changing the existing upstream OAuth login service.

Acceptance criteria:

- Add authorization-server options under a distinct `IBeam:Identity:OAuthServer` section.
- Model OAuth clients, exact redirect URIs, allowed grant types, allowed scopes, allowed resources, client type, PKCE requirements, status, and secret-hash metadata.
- Model authorization codes and user consent records needed by later cards.
- Add store interfaces for clients, authorization codes, and consents.
- Add a validated in-memory client registry suitable for tests and local development.
- Public clients require PKCE and never persist a plaintext secret.
- Unit tests cover normalization, invalid redirect URIs, unsupported grants, duplicate clients, disabled clients, and exact redirect matching.
- Existing upstream provider OAuth and API-key behavior remains unchanged.

### IBeam OAuth MCP 002: Add Azure Table OAuth authorization-server persistence

Source key: `IBM-025`

Persist OAuth clients, authorization codes, and consent records in the Azure Table identity provider.

Acceptance criteria:

- Add Azure Table entities and deterministic partition/row-key conventions for all OAuth server records.
- Implement the OAuth client, authorization-code, and consent store interfaces.
- Authorization codes are stored as hashes, expire, and can be consumed exactly once.
- Client secrets are stored only as hashes.
- Consent records are tenant, user, client, resource, and scope aware.
- Schema manager provisions the required tables without affecting existing identity tables.
- Repository tests cover round trips, expiration, one-time code consumption, revoke/disable behavior, and tenant isolation.

### IBeam OAuth MCP 003: Add Entity Framework OAuth authorization-server persistence

Source key: `IBM-026`

Provide feature parity for applications using the Entity Framework identity provider.

Acceptance criteria:

- Add EF entities and mappings for OAuth clients, authorization codes, and consents.
- Implement the same repository-independent store interfaces as Azure Tables.
- Enforce unique client ids and exact redirect URI storage.
- Authorization-code consumption is atomic and one-time.
- Consent and client queries preserve tenant and resource boundaries.
- Repository tests cover the same behavioral contract as the Azure Table provider.

### IBeam OAuth MCP 004: Resolve effective OAuth permissions

Source key: `IBM-027`

Centralize delegated permission calculation so OAuth tokens cannot exceed client, user, tenant, or consent grants.

Acceptance criteria:

- Add an effective-permission resolver shared by OAuth token issuance and claim creation.
- Resolve the intersection of requested scopes, client allowlist, subject roles/access, tenant policy, and consent.
- Reuse the existing API scope, tool, permission, module, agent, and resource conventions.
- Support wildcard entries only where the existing access catalog allows them.
- Denied or unknown scopes are excluded and reported without elevating access.
- Output normalized `scope`, `role`, `permission`, `tool`, tenant, and resource context suitable for existing MCP authorization.
- Unit tests cover allowed, partially allowed, wildcard, cross-tenant, unknown-scope, and no-consent cases.

### IBeam OAuth MCP 005: Add OAuth client administration APIs

Source key: `IBM-028`

Allow tenant administrators to manage pre-registered OAuth clients safely.

Acceptance criteria:

- Add tenant-scoped create, list, get, update, rotate-secret, disable, and revoke endpoints.
- Validate exact redirect URIs, client type, allowed grants, allowed scopes, resources, and PKCE policy.
- Return a generated confidential-client secret only once and never return its hash.
- Require existing IBeam tenant administration permissions.
- Emit auditable client lifecycle events without secret material.
- API tests cover authorization, validation, secret rotation, disable/revoke, and tenant isolation.

### IBeam OAuth MCP 006: Add asymmetric access-token signing and JWKS

Source key: `IBM-029`

Make Identity-issued access tokens independently verifiable by MCP resource servers.

Acceptance criteria:

- Support asymmetric signing keys with a stable key id and configurable rotation overlap.
- Publish public keys through a JWKS endpoint.
- Keep current issuer, tenant, session, role, and permission claims compatible.
- Validate configured signing material at startup and never expose private key data.
- Define a migration path for hosts currently using the symmetric signing key.
- Tests verify signing, JWKS verification, key ids, invalid keys, and rotation overlap.

### IBeam OAuth MCP 007: Implement authorization code, PKCE, tenant, and consent flow

Source key: `IBM-030`

Add the interactive authorization endpoint used by MCP clients acting for a user.

Acceptance criteria:

- Add an OAuth 2.1 authorization endpoint supporting `response_type=code`.
- Require and validate `client_id`, exact `redirect_uri`, `state`, PKCE S256, `scope`, and RFC 8707 `resource`.
- Require an authenticated IBeam user and resolve or prompt for tenant context.
- Show or return consent requirements based on previously granted client/resource scopes.
- Issue short-lived, hashed, single-use authorization codes bound to client, redirect URI, user, tenant, scopes, PKCE challenge, and resource.
- Return standards-shaped OAuth errors without leaking security details.
- Tests cover success, redirect mismatch, invalid resource, missing PKCE, denied consent, replay, and tenant mismatch.

### IBeam OAuth MCP 008: Implement token, refresh, and revocation endpoints

Source key: `IBM-031`

Exchange OAuth grants for resource-bound IBeam access tokens and manage their lifecycle.

Acceptance criteria:

- Add a form-encoded token endpoint for authorization-code exchange with PKCE verification.
- Issue short-lived Bearer access tokens containing subject, tenant, client id, effective scopes, audience/resource, session id, and token id.
- Support refresh-token rotation for eligible clients with reuse detection.
- Add token revocation and consent revocation behavior.
- Reject mismatched client, redirect URI, code verifier, resource, expired code, consumed code, and disabled client.
- Return OAuth-compliant token and error responses with cache-prevention headers.
- Tests cover exchange, refresh rotation, replay, revocation, audience, and least-privilege scope issuance.

### IBeam OAuth MCP 009: Publish authorization discovery and client registration metadata

Source key: `IBM-032`

Expose the metadata MCP clients need to discover and identify the IBeam authorization server.

Acceptance criteria:

- Publish OAuth Authorization Server Metadata and/or OIDC discovery from the configured issuer.
- Advertise authorization, token, revocation, JWKS, supported scopes, PKCE methods, grants, and resource-indicator support.
- Support OAuth Client ID Metadata Documents as the preferred MCP client registration mechanism.
- Add policy-controlled Dynamic Client Registration compatibility for clients that still require RFC 7591.
- Validate native versus web redirect URI rules and apply registration rate limits.
- Metadata tests verify absolute URLs, enabled capabilities, and disabled-feature behavior.

### IBeam OAuth MCP 010: Add MCP protected-resource metadata and Bearer challenges

Source key: `IBM-033`

Make `IBeam.Ai.Api` a discoverable OAuth protected resource at the HTTP boundary.

Acceptance criteria:

- Add configurable canonical MCP resource URI and authorization-server URI options.
- Publish RFC 9728 protected-resource metadata at the root and MCP-path well-known locations.
- Include supported MCP scopes without exposing tenant-specific grants.
- Return `401` with `WWW-Authenticate: Bearer` and `resource_metadata` for missing or invalid credentials.
- Return `403` with `insufficient_scope` guidance when an authenticated token lacks required scope.
- Preserve existing API-key challenges and behavior for API-key consumers.
- API tests cover metadata, challenge headers, scope guidance, and path-based MCP endpoints.

### IBeam OAuth MCP 011: Enforce dual API-key and OAuth MCP authorization

Source key: `IBM-034`

Accept either an existing IBeam API key or a valid resource-bound OAuth Bearer token and normalize both into the same MCP tool context.

Acceptance criteria:

- Add a named MCP authorization policy that accepts API-key and Bearer authentication schemes.
- Require `tool:mcp` or its configured equivalent for protected MCP access.
- Validate issuer, signature, lifetime, tenant, client status, and exact MCP audience/resource for Bearer tokens.
- Reject token passthrough and tokens issued for another MCP resource.
- Normalize OAuth scopes and roles so existing `DefaultAgentToolAccessPolicy` checks remain authoritative.
- Preserve API credential ids for API-key principals and expose OAuth client/session ids for OAuth principals.
- Tests cover both credential types, wrong audience, insufficient scope, disabled clients, and unchanged tool filtering.

### IBeam OAuth MCP 012: Add end-to-end compatibility validation and operator docs

Source key: `IBM-035`

Prove the complete flow and document safe adoption for IBeam-consuming applications.

Acceptance criteria:

- Add an end-to-end test from protected-resource discovery through authorization, token exchange, and MCP tool invocation.
- Cover public Authorization Code + PKCE clients and confidential clients where enabled.
- Cover permission intersection, tenant isolation, refresh, revoke, API-key compatibility, and OAuth error responses.
- Validate against at least one consuming MCP application that requires OAuth.
- Document Identity and MCP host configuration, key rotation, client registration, consent, troubleshooting, and migration.
- Include a security checklist for redirect URIs, resource binding, scope minimization, token logging, secrets, and revocation.

## Delivery Order

Implement one card per branch in numerical order. Use `development` as the integration branch and `codex/<source-key>` for card branches. After focused build/tests pass, merge the card into `development`, move its Hubbsly card to `done`, and then start the next card.
