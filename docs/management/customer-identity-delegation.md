# Verified support actor delegation

IBM-0100 adds server-to-server actor assertions for customer Identity management. A machine key alone never authorizes customer data operations. Support identities remain in their isolated support realm; targets never invent a customer user, membership or customer bearer token for the operator.

## Portal registration and keys

After the normal support API/customer-management registrations, call AddIBeamManagementActorDelegation(configuration). Supply IManagementActorSigningKeyProvider backed by a dedicated private RSA signing key (at least 2048 bits) in an approved secret/key store, and the durable IManagementAuditRepository. The provider returns RS256 SigningCredentials with a stable non-empty ASCII key ID. These keys are separate from customer/support login token keys and never sent to the browser. No default/generated production signing key is installed.

Configure IBeam:Management:Delegation:Issuer:Issuer, LifetimeSeconds (default 30, allowed 1-60) and Targets entries containing ApplicationId and Audience. Each application registration has one explicit target audience. Missing providers/dependencies fail startup; malformed settings fail options validation. Configuration and secrets belong to the consuming host, never repo examples or portal input.

IManagementDelegatedActorProvider.IssueAsync resolves the authenticated human, live complete operation grants, enabled app/active connection, verified realm metadata and actual target credential ID from the active secret. It signs only after rechecking authorization and connection version. There is no browser endpoint to issue arbitrary assertions.

## Target registration and trust

Install AddIBeamManagementCustomerIdentityTargetApi(configuration) and AddIBeamManagementDelegatedActors(configuration) in the target host. Supply IManagementActorValidationKeyProvider (pinned trusted issuer public RSA keys), an atomic IManagementDelegationReplayStore and the durable metadata audit repository. Configure IBeam:Management:CustomerIdentity:Delegation:TrustedPortals entries with OrganizationId, ApplicationId, Issuer and Audience. The trust map pins the support organization and portal application-registration GUID in addition to existing application key/environment/realm scope.

The validation provider is called only with an issuer already matched to the explicit trust map. No jku/x5u/token-controlled URL, signing algorithm or remote discovery is accepted. Only RS256 and ibeam-management-actor+jwt are allowed; unknown key IDs, symmetric keys, duplicate claims/headers, wrong issuer/audience/scope and assertions larger than 8 KiB fail closed. Keep clocks synchronized: there is no expiry clock skew.

Each future customer action uses the named target policy plus a server-owned ManagementCustomerIdentityOperationAttribute. The authentication handler obtains the operation from endpoint metadata, never headers or body fields. It requires exactly one X-IBeam-Management-Key and one X-IBeam-Management-Actor, validates both, and buffers at most 32 KiB of body before model binding. Customer Authorization headers and mixed credentials remain rejected. Discovery GET /api/management-identity/info is the sole machine-only metadata route.

## Bound request and live checks

The signed payload binds support organization/user, portal application registration, application key, environment, Identity realm, actual machine credential ID, requested operation, issuance/not-before/expiry, assertion ID and safe correlation. It contains no customer email/profile/body. The request digest is SHA-256 over uppercase method, exact encoded path/query and SHA-256 of the exact serialized UTF-8 body. Client and server must use the same outgoing bytes and query order; this is deliberately strict, not semantic JSON normalization. Production uses RawTarget; hosts without it use the server's encoded path/query representation.

Every target validation re-reads machine credential status/version/expiry and checks the full operation matrix, enabled target policy and installed resolving adapters. Successful verification populates a scoped read-only IManagementDelegatedActorContext. Business services must call IManagementCustomerIdentityTargetAccessService.RequireAsync inside their IServiceOperationExecutor operation before customer store access; direct calls without verified context, changed operation, expiry or live credential authority fail.

Removing a human app grant prevents new portal assertions. A previously issued read assertion can remain valid only until its signed expiry, at most 60 seconds; requests already authorized may finish. Mutation assertions are atomic single-use, including concurrent requests on different target instances. Retry with a freshly issued assertion and the same business Idempotency-Key. Actor replay protection is separate from durable business idempotency receipts added in IBM-0104.

## Replay storage and maintenance

AzureTableManagementDelegationReplayStore uses a separately provisioned ManagementDelegationReplays table. Register AddIBeamManagementAzureTableDelegationReplayStore(dedicatedTable). Runtime validation performs atomic insert only, stores no token/body/key, rejects duplicate nonce claims and never creates a table. Realm partition and issuer-hash/assertion row key separate issuers and realms. Storage failure denies the action.

Azure Table has no native TTL. Operators must run trusted bounded PurgeExpiredAsync maintenance on the dedicated table for each realm, with a cutoff at least one minute in the past; it rejects future/live cutoffs and limits deletion to one provider page and at most 1000 rows. Routine cleanup is not a portal endpoint. Retention is operationally required; otherwise expired rows accumulate while replay protection still fails closed.

## Audit attribution and scope

The metadata-only audit sink records the verified support organization/person, portal application registration and machine credential as separate IDs, keeping subjectType credential on target calls. Verified correlation joins portal issuance and target actions. Failure before actor verification is not attributed to untrusted claimed users. No JWT/assertion, machine key, request hash/body, customer profile, exception payload or fabricated customer identity is persisted. Existing audit constructor and diagnostic behavior remain compatible.

This card installs the security foundation only. Tenant/user directory and provisioning modules are subsequent cards; a package reference or an operation name alone does not expose customer endpoints. Never add test probe routes or fixture keys to production hosts.

IBM-0104 requires exactly one valid Idempotency-Key for non-GET requests and includes its digest in the signed request binding. Only explicitly marked operation receipt GETs may carry original write authority, after signature validation and stored operation/scope checks; other GETs still reject write operations. See [durable operation receipts](customer-identity-operations.md).
