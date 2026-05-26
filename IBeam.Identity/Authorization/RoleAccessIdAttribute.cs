namespace IBeam.Identity.Authorization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RoleAccessIdAttribute : Attribute
{
    public RoleAccessIdAttribute(params string[] roleIds)
    {
        RoleIds = roleIds ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> RoleIds { get; }
}
