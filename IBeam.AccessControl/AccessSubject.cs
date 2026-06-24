namespace IBeam.AccessControl;

public sealed record AccessSubject(string SubjectType, string SubjectId);

public static class AccessSubjectTypes
{
    public const string User = "user";
    public const string ApiCredential = "api-credential";
    public const string Agent = "agent";
    public const string External = "external";
}
