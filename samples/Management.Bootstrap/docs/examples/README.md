# Synthetic consumer export hook

`SyntheticTenantExporter.cs` is a Development/Testing-only app-owned provider example. The tenant-data test suite compiles this source and repeats those tests as a fresh NuGet consumer; it has no framework source project references in that generated consumer. It is not automatically enabled in the bootstrap's Identity demo and is not a Qurvia exporter.

The provider implements the exporter, approved customer delivery, native customer authorization and tenant policy ports. Its immutable fixture inventory includes facilities, settings, commercial metadata, audit history, active/removed/unseated member chats, audio and attachments. The `partial` policy explicitly retains commercial data and omits those bytes from the bundle. Removed/unseated history remains tenant-owned fixture data and is not filtered by current seats.

Supply an explicit private instance directory, isolated native realm, approved synthetic customer GUID and Development/Testing environment. Keep the framework job table/protection keys separate. The provider uses a full-scope hash directory, an exclusive publication lock, atomic immutable bundle publication and a durable approved-recipient delivery receipt. Restart reconciles the same job/files. It checks the framework lease before publication/delivery and validates a reference against that exact scope before cleanup. Generated paths are canonicalized inside the private root before deletion; filenames contain no tenant/customer names.

The app's customer route calls `IManagementTenantExportCustomerArtifacts.RequireAsync` and then `ReadForCustomerAsync` with the authenticated customer. Customer authorization, current revocation and expiry are checked again in the app-owned reader. Business bytes remain outside support status/audit. No support download endpoint, anonymous bearer URL, external delivery destination or production storage exception is included.

The fixture's point-in-time claim describes immutable synthetic data. A real consumer must choose and implement its own cross-table/blob consistency and output idempotency, customer recipient policy, physical retention, access control and durable delivery receipts; copying this file is not a production implementation. See the framework's `docs/management/tenant-export.md` for the required ports and Qurvia inventory checklist.


## Synthetic purge

`SyntheticTenantPurger.cs` is compiled by the tenant-data tests and fresh generated-NuGet consumer.
It creates only new private temporary fake files in Development/Testing for the literal `fixture`
application binding. It implements the app handler plus access/write fence, uses durable same-job
checkpoints and refuses cross-scope or Production calls. It is disabled in the running bootstrap.
The integration suite combines it with the optional native Identity bridge to prove file/data
cleanup and shared-account isolation. Retained backups/controls and excluded external systems stay
visible in coverage. See the framework tenant-purge guide for inventory and trusted recovery;
this example does not implement deletion for Qurvia or another production consumer.

## Membership projection and lifecycle composition

`SyntheticMembershipProjectionStore.cs` demonstrates durable event/repair deduplication, current
native membership reads, per-subject sequence checks, atomic transfer projection, preservation of
unrelated app fields and a shared app purge fence. It accepts only the explicit `fixture` binding,
isolated preprovisioned loopback storage and Development/Testing. No support repair route or grant
is installed. See [projection integration](../../../../docs/management/membership-projections.md).

All three examples compile in the tenant-data suite and fresh generated-NuGet consumer. They stay
disabled in the normal three-app Identity demo. Use [the lifecycle handoff](../../../../docs/management/lifecycle-consumer-handoff.md)
for optional portal composition, exact frontend routes, original-intent recovery, shared typed sender,
metadata-only telemetry and coordinated release prerequisites. Real app adapters and production
rollout require their own inventory, durable providers and all participating request/write fences.
