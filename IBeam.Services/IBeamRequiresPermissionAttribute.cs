namespace IBeam.Services.Abstractions;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class IBeamRequiresPermissionAttribute : Attribute
{
    public IBeamRequiresPermissionAttribute(string permissionName)
    {
        PermissionName = permissionName;
    }

    public string PermissionName { get; }
}

