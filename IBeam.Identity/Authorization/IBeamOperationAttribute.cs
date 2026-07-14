namespace IBeam.Identity.Authorization;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class IBeamOperationAttribute : Attribute
{
    public IBeamOperationAttribute(string key)
    {
        Key = key;
    }

    public string Key { get; }
    public string? Label { get; set; }
    public string? Description { get; set; }
    public string? Module { get; set; }
    public string? ResourceType { get; set; }
    public string? RequiredAccessLevel { get; set; }
    public string? Category { get; set; }
    public bool IsAssignable { get; set; } = true;
    public bool IsDangerous { get; set; }
}
