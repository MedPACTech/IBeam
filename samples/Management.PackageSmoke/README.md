# Package-only management smoke host

This executable is validation tooling, not a deployed portal. It references generated NuGet packages only. It creates random tables in a required loopback-only Azurite account, starts a portal and two diagnostic targets using ASP.NET TestServer, exercises public HTTP contracts, and deletes only its own fixture tables. No production credentials, public listener or consumer-host repository is used.

From the repository root, set IBEAM_MANAGEMENT_TEST_STORAGE to a private local Azurite Table connection (not a production account), then run:

```powershell
./scripts/Test-ManagementPackages.ps1
```

The script packs the eight Management modules and their IBeam project dependency closure with one private smoke version, inspects README/icon/agent prompts/contracts and checks for local secret/test artifacts. It copies this executable into an isolated build folder with fresh package restore, no repository build props and no source project references. It uses the generated local feed plus nuget.org; packages are not published. .NET 10 SDK, PowerShell 7, network/package cache access and a running private Azurite Table fixture are explicit prerequisites. Build products live in the ignored _tmp_pkg_smoke directory by default. No environment secrets are printed or persisted by the sample.

The host proves target machine authentication and non-interchangeable keys; application registration and verified connection activation; live support app assignments; a single human session switching apps; errors/logs list, detail and continuation; cross-app token replay, cross-org/customer substitution, revocation and selector removal; and portal/target audit correlation. The two Testing targets enable opposite raw module modes; raw canaries appear only inside the explicit unsanitized object and never in safe fields, credentials or audit. Targets use real Table repositories and table-specific read-only SAS for diagnostic reads.

Fixture-only SmokeAuthentication and SmokeSecrets are not production authentication or storage providers. InProcessDiagnosticsClient implements the public client port to bridge TestServer in memory; SmokeProbe verifies actual target HTTP info using the same bridge. These adapters do not bypass production network rules: production uses ApplicationDiagnosticsClient and ManagementTargetConnectionProbe with approved public HTTPS origins, DNS/socket pinning and TLS. Their transport, malformed-response, timeout, rate-limit and SSRF tests live in IBeam.Tests.Management.Applications. This smoke does not claim external TLS/network deployment validation. The normal Identity adapter's MFA, separate/shared storage and session-revocation behavior are tested in IBeam.Tests.Management.Identity; this sample demonstrates the supported bring-your-own-auth port.
