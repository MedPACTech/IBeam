# IBeam.Tests.Management.Diagnostics

Run `dotnet test IBeam.Tests.Management.Diagnostics -c Release`. Unit tests require no external resources. Set IBEAM_MANAGEMENT_TEST_STORAGE to a private Azurite Table connection to enable the three StorageIntegration cases; without it they are inconclusive. Never use production storage. Fixtures explicitly create and remove only random tables.

Tests cover 24-hour defaults, UTC/offset date boundaries, fixed-query pagination, empty/filtered continuations, row/request/time budgets, missing records, live authorization before each provider read, tag isolation and strict configuration. Token tests cover tampering, purpose/type/source/app/environment/query/key replay boundaries, expiry and nonrenewing lifetime. Real-provider tests inspect exact partition/date filters and column selection, preserve row ETags, verify no reader writes/schema creation, and exercise table-scoped read-only SAS.

Run IBeam.Tests.Management.Credentials and IBeam.Tests.Management for shared service/repository and API boundary regressions. No public system-error routes or capability advertisement are introduced by this card.
