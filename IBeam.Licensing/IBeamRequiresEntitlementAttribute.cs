namespace IBeam.Licensing;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class IBeamRequiresEntitlementAttribute : Attribute
{
    public IBeamRequiresEntitlementAttribute(string entitlement)
    {
        Entitlement = entitlement;
    }

    public string Entitlement { get; }
}
