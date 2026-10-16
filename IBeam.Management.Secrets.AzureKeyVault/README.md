# IBeam.Management.Secrets.AzureKeyVault

Optional writable IManagementSecretStore for portal connection keys. Register AddIBeamManagementKeyVaultSecrets with a dedicated SecretClient constructed by the host. Grant that managed identity secret get/set access only to the management vault. Never enable SDK HTTP content logging or log portal request bodies/management key headers.

Each submission creates a unique application/organization-scoped secret with a versioned reference. References cannot choose a vault, another application or an unversioned latest value. Rows contain only references; secret values are excluded from serialization and ToString. Provider errors are translated without SDK messages, response bodies or inner exceptions.

No vault creation, deployment, deletion, purge or secret enumeration is performed. Failed validation or a lost concurrent activation can leave an unreferenced secret/version or verified metadata row; retain and clean those through separately authorized operational tooling. Never remove a secret still referenced by an active connection.

Client setup and managed identity guidance: https://learn.microsoft.com/en-us/dotnet/api/overview/azure/security.keyvault.secrets-readme
