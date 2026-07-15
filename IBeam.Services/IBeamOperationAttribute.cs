namespace IBeam.Services.Abstractions;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class IBeamOperationAttribute : Attribute
{
    public IBeamOperationAttribute(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public string? AuditAction { get; set; }

    public string? PermissionName { get; set; }

    public bool Audit { get; set; } = true;

    public bool Permission { get; set; } = true;
}

