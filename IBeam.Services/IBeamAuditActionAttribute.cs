namespace IBeam.Services.Abstractions;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class IBeamAuditActionAttribute : Attribute
{
    public IBeamAuditActionAttribute(string action)
    {
        Action = action;
    }

    public string Action { get; }

    public bool Enabled { get; set; } = true;
}

