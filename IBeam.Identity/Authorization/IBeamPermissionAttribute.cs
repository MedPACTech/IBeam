namespace IBeam.Identity.Authorization;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class IBeamPermissionAttribute : Attribute
{
    public IBeamPermissionAttribute(string key)
    {
        Key = key;
    }

    public string Key { get; }
    public string? Label { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public bool IsAssignable { get; set; } = true;
    public bool IsDangerous { get; set; }
}
