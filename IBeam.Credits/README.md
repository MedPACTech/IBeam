# IBeam.Credits

`IBeam.Credits` contains provider-neutral credit accounting contracts and models for IBeam-backed applications.

Credits are intentionally generic. A host can use them for AI chat, dictation, document processing, SMS, workflow runs, storage, or any other metered capability.

```powershell
dotnet add package IBeam.Credits
```

## Core Concepts

| Concept | Meaning |
|---|---|
| Bucket | A host-defined consumption category such as `ai-chat`, `sms`, or `workflow-runs`. |
| Account | A tenant-owned credit account, optionally tied to a user, agent, API credential, or external subject. |
| Grant | A positive credit allocation such as one-time, monthly, expiring, or rollover-capable credits. |
| Ledger entry | An append-only accounting entry. Grants are positive and debits are negative. |
| Balance | A calculated view of granted, debited, expired, and available credits for one bucket. |

## Grant Types

`CreditGrantInfo` supports common credit grants without naming them tokens:

- `one-time`
- `monthly`
- `expiring`
- `adjustment`

Rollover behavior is represented separately with `CreditRolloverPolicies`: `none`, `rollover`, or `capped-rollover`. Host-specific rollover caps or renewal metadata belong in `Metadata`.

## Append-Only Ledger

The core store contract only exposes `AppendLedgerEntryAsync` and ledger reads. Services may add reservations and settlement workflows later, but the accounting model should preserve history rather than mutating prior entries.

## Package Relationships

| Package | Role |
|---|---|
| `IBeam.Credits` | Core credit buckets, accounts, grants, ledger entries, balance calculations, and store contract. |
| `IBeam.Credits.Services` | Future reservation, settlement, policy-mode, and local store services. |
| `IBeam.Credits.Api` | Future runtime/admin balance and ledger endpoints. |
