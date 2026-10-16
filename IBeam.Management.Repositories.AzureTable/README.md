# IBeam.Management.Repositories.AzureTable

Optional repository adapter for IBeam Management. It depends on core contracts and Azure Tables, with no Services, API or Identity dependency.

ManagementApiCredentials stores credential ID, application/environment, a versioned SHA-256 secret hash, explicit capabilities, Active/Revoked status, UTC creation/expiry/rotation/revocation and operator IDs. Raw API keys are never persisted. Point reads use DEPLOY|SHA256(JSON([application, environment])) and CRED|credentialId; no cross-deployment scans or secondary secret indexes. Updates compare the supplied ETag; concurrent rotation/revocation cannot overwrite one another.

Register AddIBeamManagementAzureTableCredentialStore(configuration), using IBeam:Management:Credentials:Storage:ConnectionString from a secret store and Storage:TableName (default ManagementApiCredentials). This connection is independently configured; it never falls back to customer/Identity storage. Alternatively supply a dedicated TableClient with a managed identity or table-scoped SAS. Registration never replaces a customer's TableClient.

Trusted deployment tooling creates the table explicitly using a privileged TableClient.CreateIfNotExistsAsync. Runtime registration and reads never provision schema. Runtime validation requires read permission only; trusted provisioning uses a separate write-capable credential/tool. Prefixes and table names alone are not storage authorization boundaries; choose separate accounts or properly scoped Azure credentials. The repository propagates infrastructure errors, and the caller fails closed.

Tests use private Azurite with random per-test tables, explicit setup/cleanup, real ETags and deployment-isolation checks. No cloud resources or production table operations are required.
