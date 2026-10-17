# Management applications and access tests

Run dotnet test IBeam.Tests.Management.Applications/IBeam.Tests.Management.Applications.csproj -c Release. Set IBEAM_MANAGEMENT_TEST_STORAGE to private Azurite for the two application/connection storage integration fixtures; absent configuration marks them inconclusive.

Coverage includes optional registry/connection routes, safe envelopes/body logging, approved HTTPS destinations, public DNS checks and IP pinning, environment and mocked Key Vault adapters, verified rotations, ETag conflicts and organization isolation. Assignment tests cover zero/one/many apps, independent errors/logs, explicit admin/delegation ceilings, no administrator diagnostic bypass, live disable/revoke checks, scope/row tampering, bounded pagination, stale versions, cancellation and sanitized provider errors.

Real Identity/MFA assignment HTTP and real access-table fixtures live in IBeam.Tests.Management.Identity. No test sends production communications or uses live cloud secrets.
