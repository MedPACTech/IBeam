namespace IBeam.Identity.Authorization;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class IBeamOperationTemplateAttribute : Attribute
{
    public IBeamOperationTemplateAttribute(string template)
    {
        Template = template;
    }

    public string Template { get; }
    public string? Operation { get; set; }
    public string? LabelTemplate { get; set; }
    public string? DescriptionTemplate { get; set; }
    public string? Category { get; set; }
    public bool IsAssignable { get; set; } = true;
    public bool IsDangerous { get; set; }
}
