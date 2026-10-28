# Handoff prompt: IBeam Management application

Build the tenant and customer-user management screens in the existing frontend stack against the running **IBeam Management API**. This is an independent bootstrap application: configure its API URL, authenticate a support person, select an assigned application, and call that API. The frontend does not need the framework source or a backend repository checkout.

Local enterprise demo API: `http://127.0.0.1:5155`. Check `GET /health` and the live Development specification at `GET /openapi/v1.json`. Allowed local frontend origins are `http://127.0.0.1:5145` and `http://localhost:5145`. The operator starts the API using the framework's `samples/Management.Bootstrap/.scripts/start-enterprise-demo.ps1`; `-HostRoot` can point at the temporary `IBeam.ADMIN.API` host. Do not start or silently mock a missing API from browser code. API packages are the stable `2.16.0` release from NuGet.org.

The temporary host's private `.local/management-enterprise/operator.json` and `demo-users.json` contain local support credentials. Real MFA messages are in `.local/management-enterprise/outbox`. The operator may use these files for local interactive tests; never paste, log, commit, expose over HTTP or embed their contents. `reader@example.test` has all-app customer management; `limited@example.test` has diagnostic errors only for Atlas; `unassigned@example.test` has no app assignment. Passwords are generated privately. Customer passwords, target keys, signing material and storage settings remain backend/operator-only.

Authenticate with `POST /api/auth/password-login` `{email,password}`. Require the MFA step when `data.requiresTwoFactor` is true; submit `{email,challengeId,code}` to `POST /api/auth/2fa/complete-login`. Tokens are under **`data.token`**, including `accessToken`, `refreshToken`, `expiresAt` and `sessionId`. Send `Authorization: Bearer <accessToken>` to management routes. Keep the existing refresh/logout handling; refresh is `/api/auth/refresh`, logout is `/api/auth/logout`. A customer application's JWT and any API key cannot replace this support login. Clear personal operation references and app-specific state on logout/session changes.

Load the dropdown from `GET /api/management/applications?pageSize=50`. Each returned item has `applicationId`, `applicationKey`, `name`, `environment` and current `permissions`. Follow opaque continuation tokens. After selecting an app, call `GET /api/management/applications/{app}/customer-management` and honor its module availability/capabilities. Clear tenant selection and protected cursors when switching apps. Keep one support session across apps. Hide/disable actions without the corresponding human permission; the server remains authoritative and can revoke access during a session. On 401 reauthenticate; on 403 discard protected results and show loss of access.

All paths below use `P = /api/management/applications/{app}`:

| Action | Method and path | Body / required operation |
|---|---|---|
| Tenants | GET `P/tenants`, GET `P/tenants/{tenant}` | `management.identity.tenants.read` |
| Create tenant | POST `P/tenants` | `{name,externalReference?}`; `tenants.create` |
| Rename tenant | PATCH `P/tenants/{tenant}` | `{name}`; `tenants.update` |
| Deactivate/reactivate | POST `P/tenants/{tenant}/deactivate` or `/activate` | `{}`; `tenants.status` |
| Tenant users | GET `P/tenants/{tenant}/users`, GET `.../users/{user}` | `users.read` |
| Invitations | GET `P/tenants/{tenant}/invites`, GET `.../invites/{invite}` | `invites.read` |
| Approved roles | GET `P/tenants/{tenant}/assignable-roles` | `roles.read`; rows include roleId, name, policyKeys |
| Provision customer | POST `P/tenants/{tenant}/users/provision` | `{email,policyKey,roleIds}`; `users.provision` + `invites.manage` + `roles.assign` |
| Resend/revoke invite | POST `P/tenants/{tenant}/invites/{invite}/resend` or `/revoke` | `{}`; `invites.manage` |
| Membership status | PATCH `P/tenants/{tenant}/users/{user}/membership` | `{isEnabled}`; `memberships.manage` |
| Replace local roles | PUT `P/tenants/{tenant}/users/{user}/roles` | `{policyKey,roleIds}`; `roles.assign`; empty array removes allowed local roles |
| Onboarding policies | GET `P/enterprise-onboardings/policies` | `onboarding.read`; rows contain key/requiresInitialization |
| Enterprise onboarding | POST `P/enterprise-onboardings` | `{tenantName,initialAdminEmail,externalReference?,policyKey}`; `onboarding.create` + `tenants.create` + `invites.manage` + `roles.assign` |
| Explicit recovery | POST `P/enterprise-onboardings/{operationId}/retry` | `{}`; `onboarding.retry` plus the full create composite |
| Poll a write | GET `P/operations/{reference}?authority={originalOperation}` | The same original write authority used to create that protected reference |

Operation abbreviations in the table all begin with `management.identity.`. Independent role catalog and onboarding policy reads are not granted automatically by write permission. Demo policy keys are `enterprise-owner` for onboarding, `member` for Member provisioning/role changes and `administrator` for Owner assignment. Discover actual keys and IDs from the target catalogs; never hardcode customer role IDs, allow arbitrary role names, or send profile/redirect/credential JSON.

Every write needs a new `Idempotency-Key` for a **new intent** (8–128 ASCII letters/digits or `-_.`). Keep that same key and exact body when retrying an uncertain request. Tenant rename/status, membership/role changes and invitation resend/revoke also need the exact opaque `version` from the current resource in `If-Match`. Do not reformat/strip quotes from Azure ETags. A 409 may indicate changed intent or a last-admin conflict; a 412 requires rereading the resource and deliberate resubmission. Support-user assignment endpoints use their existing body `expectedVersion` contract instead.

Responses use `{success,data,errors,traceId,...}`. List `data` is an array with top-level `pageSize` and `continuationToken`; page sizes are 1–100. Lists can contain zero visible rows **and still have a continuation token**. Follow the token without treating it as a count or offset. Tenant filters are `status=Active|Disabled` and bounded `namePrefix`; user status is `active|disabled`; invitation directory status uses the documented native values. Cursors are bound to the selected app/person/query. Provenance is `support-provisioned`, `self-service` or `unknown`; absent evidence is unknown. Server-side source filtering is not supported.

Enterprise writes return `data` as operation tracking `{reference,status,operation?,expiresUtc}`. Tenant mutations return `{tracking,tenant?,source?,externalReference?}`; user provisioning/invite changes return `{tracking,status,invitation?}`; membership changes return `{tracking,membership?}`. Poll using the protected reference (or response Location) and original full operation name, not a guessed target URL. A 202 or `unknown` with a null operation is a pending/uncertain result, not failure. Keep the original intent/key, poll with backoff and avoid automatic new-key retries. References expire and are bound to the support person/app/connection; a 404/expired reference does not prove a customer write failed.

Generic receipt `status=succeeded` means processing completed. Enterprise completion additionally requires `operation.onboarding.completed=true`, proved from verified customer acceptance and an active approved membership. `awaiting-acceptance` is distinct from completed. Mail `sent` never proves acceptance. Display pending/sending/sent/failed/unknown delivery separately from accepted (`redeemed` in native directory views), revoked and expired invitation states. Initialization failures remain recoverable and block invitations. Unknown delivery requires explicit recovery/resend; never send mail merely by polling.

Support onboarding creates no immediate customer membership. Customer identity proof and invite acceptance belong to the **customer app's own Identity API** (`/api/invites/accept`), with its own verified session/password/OTP flow. Do not accept an invitation with the support JWT, show customer passwords, fetch private mail from HTTP, or invent a browser bypass. The support UI shows safe progress and offers authorized retry/revoke actions; customer-facing links and redirects are target-owned.

Show tenant and membership status independently. Disabling a tenant preserves its users and data; disabling one membership preserves the same account's other tenants. Re-enabling does not restore removed roles or old tokens. Surface last-administrator conflicts clearly. Expose IBeam tenant/user/invitation/diagnostic metadata only; do not add customer business-record viewers, global-account disable controls or unrestricted database queries.

Use the existing diagnostics, app registry, connection and support-user/assignment screens. A connection key is a backend secret stored by an authorized operator workflow; ordinary frontend configuration and customer-management calls need only the API URL and normal support session. Do not persist machine keys in browser storage or code. Build the frontend and its normal UI tests; backend feature implementation, new authentication shortcuts, production deployment and package publication are outside this handoff.

## Ownership integration (upcoming release)

Use the independent `management.identity.owners.read` and `management.identity.owners.transfer` grants and fixed scoped routes described in [ownership migration](../../../docs/management/ownership-consumer-migration.md). Keep `users.read` independent for destination selection. Bind the current ownership version through If-Match and include both membership versions in the strict transfer body. Preserve original intent/key on uncertain retries and poll the returned Location without repeating a transfer. Do not derive the owner badge from Tenants.OwnerUserId, expose machine keys, or treat roles.assign as transfer authority. Stable 2.16.1 lacks these endpoints; use a matching validated development package feed until the coordinated ownership release.


## Optional tenant purge (upcoming release; separately enabled target/portal)

Use independent `management.tenants.delete` authority, not tenant status or export permission.
Routes under `/api/management/applications/{app}/tenants/{tenant}` are POST `/purge-plans`
with `{policyKey}`, POST `/purges` with `{policyKey,planId,confirmationBinding}`, and GET
`/purges/{jobId}`. Both POSTs require the original exact tenant `If-Match` and separate stable
idempotency keys. Show the returned inventory, retained/excluded categories and short plan expiry;
confirm only after explicit human intent. A valid plan is bound to the original support person,
app/realm/tenant, policy, inventory and native version. A changed/expired plan needs fresh preflight.

An unknown preflight requires a valid explicitly recovered plan before confirmation. An unknown
confirmation preserves its original operation ID and status Location: poll it without creating a
new destructive intent. Queued/fencing/purging/verifying, partial, failed, cancelled and unknown
remain distinct from succeeded; display retained/excluded coverage even after success. There is
no support resume, cancellation or global-account deletion endpoint. The consumer owns app data
cleanup and all request/write fences. This capability is not installed in the running demo; use
an explicitly configured test instance and the tenant-purge guide. Stable 2.16.1 lacks it.
