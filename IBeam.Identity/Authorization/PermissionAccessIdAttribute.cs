namespace IBeam.Identity.Authorization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class PermissionAccessIdAttribute : Attribute
{
    public PermissionAccessIdAttribute(params string[] permissionIds)
    {
        PermissionIds = permissionIds ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> PermissionIds { get; }
}
