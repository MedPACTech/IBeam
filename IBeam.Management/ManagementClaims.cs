namespace IBeam.Management;

/// <summary>Server-issued management context. Ordinary customer roles are never a substitute.</summary>
public static class ManagementClaimTypes
{
    public const string SubjectType = "ibeam_management_subject_type";
    public const string OrganizationId = "ibeam_management_organization_id";
    public const string CredentialId = "ibeam_management_credential_id";
    public const string ApplicationKey = "ibeam_management_application_key";
    public const string Environment = "ibeam_management_environment";
    public const string Permission = "ibeam_management_permission";
}

public static class ManagementSubjectTypes
{
    public const string Human = "human";
    public const string Credential = "credential";
}

public static class ManagementOperations
{
    public const string IdentityRead = "management.identity.read";
    public const string DiagnosticsInfo = "management.diagnostics.info";
}
