namespace IBeam.Identity.Authorization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class PermissionAccessAttribute : Attribute
{
    public PermissionAccessAttribute(params string[] permissionNames)
    {
        PermissionNames = permissionNames ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> PermissionNames { get; }
}
