namespace IBeam.Identity.Authorization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RoleAccessAttribute : Attribute
{
    public RoleAccessAttribute(params string[] roleNames)
    {
        RoleNames = roleNames ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> RoleNames { get; }
}
