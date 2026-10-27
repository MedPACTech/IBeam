# Portal customer provisioning and invitation management (IBM-0111)

Register `services.AddIBeamManagementPortalCustomerProvisioningApi(configuration)` in the support Management API. This opts into the existing safe customer directory routes, role catalog, provisioning and invitation transitions plus protected operation tracking. Supply the existing server-side selected-app connection/secret store, support authentication, live assignments, delegated signing provider, approved HTTPS origins and portal cursor/tracking protection key. No customer Identity or customer storage is registered in the portal. Targets separately install the IBM-0109 provisioning/catalog and IBM-0110 lifecycle modules, explicit role/link policies, native stores and dedicated journals. Existing keys/assignments must be explicitly granted the new capabilities; registration never broadens them.

All paths below start with `/api/management/applications/{applicationId}/tenants/{tenantId}`. The app selector uses the support user's current assigned applications. Tenant IDs, role IDs and policies belong to the selected app/environment; IDs may overlap across isolated apps and must never be reused from a different selection.

| Method and suffix | Human operation | Input |
| --- | --- | --- |
| GET `/assignable-roles` | `management.identity.roles.read` | None; maximum 32 approved existing roles with target-owned policy keys |
| POST `/users/provision` | `management.identity.users.provision` plus invites.manage and roles.assign | Email, policyKey, 1–8 approved roleIds, Idempotency-Key |
| GET `/invites` and `/invites/{inviteId}` | `management.identity.invites.read` | Existing bounded directory filters/cursor |
| POST `/invites/{inviteId}/resend` | `management.identity.invites.manage` | Strict `{}`, Idempotency-Key and exact If-Match |
| POST `/invites/{inviteId}/revoke` | `management.identity.invites.manage` | Strict `{}`, Idempotency-Key and exact If-Match |

These are independently granted operations. Role visibility does not grant provisioning/role assignment. A provisioner without roles.read can submit only policy/role IDs supplied through trusted target configuration; the server still validates them. A provisioner without invites.read can poll its own scoped provisioning receipt's safe invitation projection. Support administrators have no bypass. Authorization runs before connection secrets are resolved, the target verifies current machine/host policy, and a fresh live portal check runs after the outbound attempt. A role catalog response never reserves future authority: losing the assignment or host policy before submit denies the write.

Provisioning is invitation-only. Target Identity internally reuses an existing customer identity at verified acceptance; the portal has no customer account table, email-existence lookup, password, MFA reset, global disable, impersonation or direct database connection. No request can supply recipient replacement on resend, arbitrary role names, resource access grants, redirects, profile bags, target URLs or keys. The target reuses its own trusted link/message policy. Safe typed reconstruction drops extra upstream fields and rejects mismatched tenant/invitation/operation IDs, invalid states and unbounded role metadata.

```javascript
// apiBase is the support Management API; authHeaders come from normal support login/MFA.
const base = `${apiBase}/api/management/applications/${selectedAppId}/tenants/${selectedTenantId}`;
const roles = (await (await fetch(`${base}/assignable-roles`, { headers: authHeaders })).json()).data;
// Render roles[].name and target-owned roles[].policyKeys; never invent Admin/Owner IDs.
const response = await fetch(`${base}/users/provision`, {
  method: 'POST', headers: { ...authHeaders, 'Content-Type': 'application/json', 'Idempotency-Key': requestId },
  body: JSON.stringify({ email, policyKey: selectedPolicyKey, roleIds: selectedRoleIds })
});
const result = (await response.json()).data;
// Read result.status and result.tracking.operation?.invitation, then poll the Location URL.
// Keep the same requestId if intentionally replaying this exact intent; a changed intent needs a new ID.
```

Mutations return `{tracking, status, invitation}` in the IBeam envelope. The safe invitation contains email, approved role metadata, native status/timestamps and current version. Tracking contains an opaque personal/app/connection-bound reference and an optional processing receipt. A receipt's `succeeded` means processing, never verified customer acceptance or confirmed email delivery. Display separate delivery states pending/sending/sent/failed/unknown, and terminal customer states accepted/expired/revoked. Native directory state uses `redeemed`; receipt projection normalizes it to `accepted`. Delivery failures/uncertainty are explicit even with a successful HTTP response. Provisioning normally returns 202 until accepted; completed lifecycle processing may return 200 with failed/unknown delivery. HTTP success does not mean the customer joined.

Every mutation supplies Location for protected GET `/api/management/applications/{applicationId}/operations/{reference}?authority={originalOperation}`. Poll with normal support authentication and the original full operation name. After a timeout/malformed/503 reply the portal returns unknown tracking without automatically retrying the write. Poll resolves the original scoped idempotency identity and reads an existing receipt. Responses may include `operation.invitation = {tenantId, inviteId, status, deliveryStatus, version}` from bounded authoritative target point reads. This projection contains no email, token, URL or encrypted recovery input. Polling does not decrypt recovery input, create membership, resume workflows, or send email. Missing target projection remains unknown; it is never promoted from generic succeeded processing into business success. Replace the stored reference with the newest returned reference; its expiry remains fixed, normally 15 minutes. Changing person, app, realm, credential or connection invalidates it. Revoked authority denies subsequent polling. A 404 lookup does not prove an in-flight write failed.

For resend/revoke, read current invitation detail if allowed, then POST strict `{}` with its exact `version` in If-Match and a fresh intent key. Same-key replay must retain the original version; a new action or refreshed-version intent needs a new key. Handle 412 by fetching the current version, 409 as an ineligible or conflicting state, and 429 by waiting for the target resend policy. Do not silently retry after an uncertain SMTP outcome. The native customer app owns verification and acceptance; the browser receives no redemption token or link through management.

Use ordinary text rendering for role/policy labels and all customer-facing strings. Never log request bodies, email, tokens, protected references, Authorization, machine headers or provider errors. Management audit records allowlisted action/result metadata and trusted organization/app/person/tenant IDs only. The temporary bootstrap/sample host is integrated in IBM-0117 after full lifecycle verification in IBM-0116.
