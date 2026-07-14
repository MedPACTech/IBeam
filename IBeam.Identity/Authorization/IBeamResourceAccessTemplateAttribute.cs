namespace IBeam.Identity.Authorization;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class IBeamResourceAccessTemplateAttribute : Attribute
{
    public IBeamResourceAccessTemplateAttribute(string resourceTypeTemplate)
    {
        ResourceTypeTemplate = resourceTypeTemplate;
    }

    public IBeamResourceAccessTemplateAttribute(string resourceTypeTemplate, string idParameter, string accessLevel = "view")
        : this(resourceTypeTemplate)
    {
        IdParameter = idParameter;
        AccessLevel = accessLevel;
    }

    public string ResourceTypeTemplate { get; }
    public string? IdParameter { get; set; }
    public string AccessLevel { get; set; } = "view";
}
