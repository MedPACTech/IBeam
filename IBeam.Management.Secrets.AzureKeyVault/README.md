# IBeam.Management.Secrets.AzureKeyVault

Optional writable IManagementSecretStore for portal connection keys. Register AddIBeamManagementKeyVaultSecrets with a dedicated SecretClient constructed by the host. Grant that managed identity secret get/set access only to the management vault. Never enable SDK HTTP content logging or log portal request bodies/management key headers.

Each submission creates a unique application/organization-scoped secret with a versioned reference. References cannot choose a vault, another application or an unversioned latest value. Rows contain only references; secret values are excluded from serialization and ToString. Provider errors are translated without SDK messages, response bodies or inner exceptions.

No vault creation, deployment, deletion, purge or secret enumeration is performed. Failed validation or a lost concurrent activation can leave an unreferenced secret/version or verified metadata row; retain and clean those through separately authorized operational tooling. Never remove a secret still referenced by an active connection.

Client setup and managed identity guidance: https://learn.microsoft.com/en-us/dotnet/api/overview/azure/security.keyvault.secrets-readme

Deployment configuration, identity storage choices, MFA/bootstrap, onboarding, exact registrations/endpoints, network/proxy policy, secret rotation, audit retention/failure behavior and the deferred consumer host are covered by the [Management deployment handoff](https://github.com/MedPACTech/IBeam/blob/development/docs/management/deployment-and-handoff.md). [Source/history isolation](https://github.com/MedPACTech/IBeam/blob/development/docs/management/diagnostic-source-isolation.md) describes which historical rows may be exposed. Generated-package validation uses the package-only two-target smoke sample; IBM-0097 supplies independent host-owned errors/logs sanitization switches, safe by default and restricted to explicit non-Production contexts. See [sanitization settings](https://github.com/MedPACTech/IBeam/blob/development/docs/management/diagnostic-sanitization.md).
