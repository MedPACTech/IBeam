namespace IBeam.Identity.Authorization;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class IBeamResourceAccessAttribute : Attribute
{
    public IBeamResourceAccessAttribute(string resourceType)
    {
        ResourceType = resourceType;
    }

    public IBeamResourceAccessAttribute(string resourceType, string idParameter, string accessLevel = "view")
        : this(resourceType)
    {
        IdParameter = idParameter;
        AccessLevel = accessLevel;
    }

    public string ResourceType { get; }
    public string? IdParameter { get; set; }
    public string AccessLevel { get; set; } = "view";
}
