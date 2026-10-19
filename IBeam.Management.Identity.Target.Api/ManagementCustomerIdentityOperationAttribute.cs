namespace IBeam.Management.Identity.Target.Api;

/// <summary>Server-owned operation mapping. Never select the operation from an actor/request header.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ManagementCustomerIdentityOperationAttribute(string operation) : Attribute
{
    public string Operation { get; } = operation;
}
