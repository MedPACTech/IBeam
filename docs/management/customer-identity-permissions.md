# Customer Identity operation grants

IBM-0099 extends assignment and machine-credential allowlists. Existing credentials and assignments gain no additional permissions. No customer data/write endpoints are installed by this card; IBM-0100 supplies verified delegated actors before directory/provisioning services are exposed.

## Required operation matrix

All names below use the management.identity. prefix. Each operation requires its own explicit human app assignment, target machine credential capability, installed target module/adapter and target-owned enabled-operation policy. Composite actions require every listed grant on both human and machine sides; removing any prerequisite prevents execution.

| Requested operation | Required grants |
| --- | --- |
| tenants.read | tenants.read |
| tenants.create | tenants.create |
| tenants.update | tenants.update |
| tenants.status | tenants.status |
| users.read | users.read |
| users.provision | users.provision, invites.manage, roles.assign |
| invites.read | invites.read |
| invites.manage | invites.manage |
| memberships.manage | memberships.manage |
| roles.assign | roles.assign |
| onboarding.create | onboarding.create, tenants.create, invites.manage, roles.assign |
| onboarding.read | onboarding.read |
| onboarding.retry | onboarding.retry, onboarding.create, tenants.create, invites.manage, roles.assign |

info is machine discovery only and is not assignable as a human customer operation. management.identity.read is the existing support-session identity operation, not a customer directory grant. Errors/logs remain independent and imply none of the above. No wildcard, Owner/Admin role name or management administrator bypass exists.

Incomplete composite assignment sets may be stored but cannot execute the composite action. User provisioning initially always requires the approved role-assignment authority because its server-owned policy determines membership roles. An assignment catalog is an authorization ceiling, not a license, billing grant or customer tenant membership.

## App-specific delegation ceilings

To assign/revoke customer operations, a human must be a live support administrator, hold management.application-access.admin and have the exact per-app/per-operation delegation permission for every old and new permission on the assignment. Old permissions are also checked to prevent replacing a stronger assignment with a weaker one without delegation authority.

For a customer operation, the delegation claim is management.application-access.delegate.identity.<operation-suffix>:<applicationId-N>. Example suffix: tenants.read. An unscoped claim, another app's claim, wildcard, customer role or broad diagnostic delegation is insufficient. Self-assignment has the same checks. Delegation authority does not itself grant customer data access; an actual application assignment is still required.

With the optional built-in support Identity adapter, configure IBeam:Management:Identity:CustomerDelegations as a list of UserId, ApplicationId and Operations arrays. Entries are trusted operator settings, not portal input. Each Operations entry is a full assignable management.identity operation. Missing/empty configuration grants nothing new. Invalid/duplicate bindings or operations fail validation. Only the named user while still a live ManagementAdministrator receives the scoped claims.

Support login/MFA enrichment includes configured ceilings. Every validated bearer request removes stale role/permission claims and regenerates ceilings from current validated options and live membership; removing a configured ceiling or administrator membership therefore does not require a new login. IOptionsMonitor-backed configuration providers may reload settings; environment-variable configurations require a restart. Bring-your-own support authentication must supply equally trustworthy current app-scoped ceiling claims and live identity state.

## Target-owned policy and capability projection

Configure IBeam:Management:CustomerIdentity:EnabledOperations on each target. The default empty list permits discovery only. Every entry is an explicit full operation name; wildcards, duplicates and info are rejected. Changing target policy configuration requires a host restart.

Target discovery advertises only the intersection of explicit machine grants, enabled policy and resolving installed adapters. A composite capability also requires its prerequisite modules to be installed and advertised. Defining a capability name does not install routes or bypass actor verification.

Connection metadata stores the verified target policy/module snapshot. Customer-management discovery and the app selector intersect that snapshot with current human assignments and machine metadata; unsupported/incomplete operations do not appear as effective permissions. The portal IManagementCustomerIdentityAuthorizationService checks the complete matrix through IServiceOperationExecutor, rechecking app/actor/grants before return without resolving a machine key. Identity-only users can select assigned Identity-capable apps.

Connection snapshots are not a live target-policy cache for customer operations: subsequent target services must recheck live machine status, target policy and verified delegated actor on every request (IBM-0100). Target capability/policy changes require connection re-verification/replacement to update displayed availability; stale metadata never authorizes an operation the target now denies. Support account, app, assignment and connection changes are checked without a login refresh.
