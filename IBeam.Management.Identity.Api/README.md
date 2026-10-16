# IBeam.Management.Identity.Api
Optional Identity-backed support authentication for a dedicated management API host. Reference this package only when choosing IBeam Identity instead of bring-your-own-auth.

Register an IIdentityCommunicationSender that securely delivers MFA email, then:
```csharp
builder.Services.AddIBeamManagementIdentityApi(builder.Configuration);
var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
```

This registers the optional Identity services/provider and the portal API. Do not also call AddIBeamIdentityApi/AddIBeamIdentityApiControllers or duplicate AddIBeamManagementIdentityServices in this host. Generic Identity account/enrollment/OTP/reset/tenant-selection endpoints are intentionally not installed.

Use IBeam:Management:Identity settings from the services package:
- Explicit support OrganizationId and isolated Storage settings; BootstrapEnabled=false and Storage:ProvisionSchema=false are required at runtime.
- Jwt:Issuer / Jwt:Audience default to IBeam.Management.Identity / ibeam-management; configure different trust settings from customer tokens. A configured customer audience cannot equal the management audience.
- Jwt:SigningKey (minimum 32 characters) or asymmetric signing settings from a secret store. No sample secret is supplied.
- AccessTokenMinutes defaults to 10 (maximum 15), SessionInactivityMinutes to 30 (maximum 60), SessionAbsoluteLifetimeDays to 1 (maximum 1); remember-device extension is not exposed.
- Otp:HashSalt and Otp:VerificationTokenSecret must each be dedicated secrets of at least 32 characters; the provider default salt is rejected. CodeLength >= 6, MaxAttempts 1-10 and enabled password-attempt lockout are required.
- Strict lifecycle guards and closed auto-enrollment are enforced by the adapter.

Routes:
- POST /api/auth/password-login: email/password -> an MFA challenge only.
- POST /api/auth/2fa/complete-login: email/challengeId/code -> management access + refresh tokens after successful MFA.
- POST /api/auth/refresh: rotate a still-active management refresh token.
- POST /api/auth/logout: revoke the authenticated user's current session.
- POST /api/management/support-users/{userId}/disable: an active ManagementAdministrator disables another person's support membership and revokes their organization sessions. Self-disable is rejected to preserve administrative recovery.
- GET /api/management/me: the existing protected management identity endpoint.

No public self-registration, invitation issuance/acceptance, password recovery, credential mutation, OAuth/OTP-only login or customer-user provisioning is added. The later support invitation feature must validate one-time invitations before creating support users; it cannot simply expose generic registration.

The named bearer scheme validates signature, algorithm, issuer, audience and expiration. Each request additionally checks persisted session, MFA proof, current user, active support organization/membership and a current ManagementAdministrator/ManagementSupport role. Removed roles, disabled memberships/organizations, missing MFA and revoked/expired sessions fail on the next request; no authorization cache or access-token-expiry wait is used. Refresh repeats those checks and claims enrichment, so stale grants do not reappear.

Membership/session storage outages fail closed. Concurrent requests already authorized before a disable may complete; subsequent requests recheck state. Use durable audit and the deployment/network policies specified by later epic features before production exposure.

IBeam.Admin.API remains a deferred NuGet-consuming host. This package does not rename/edit that repository or deploy anything.

## Optional support administration routes

After AddIBeamManagementIdentityApi, call AddIBeamManagementSupportUsersApi(). Routes are absent without this opt-in:

- GET /api/management/support-users: management-only directory.
- POST /api/management/support-invitations: email; returns inviteId/expiresUtc/status.
- POST /api/management/support-invitations/accept: anonymous, write-only inviteToken/password; returns userId/requiresMfaLogin without a session.
- PATCH /api/management/support-users/{userId}: isEnabled=false only; reenabling is rejected.

Administrative routes require a live human management session and management.support-users.admin. Organization and role assignment always come from server-owned management configuration. Expected errors use safe envelopes (400 invalid input/invitation, 403 denied, 503 unavailable); no account secrets or provider messages are returned. Acceptance disables HTTP body logging and limits requests to 16 KiB. Custom host telemetry must also omit invitation tokens/passwords. Invitation delivery failures are sanitized before nested Identity audit. Generic Identity enrollment/recovery/invite controllers remain absent.
