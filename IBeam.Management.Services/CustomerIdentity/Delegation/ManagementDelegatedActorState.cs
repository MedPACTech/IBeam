using IBeam.Management.CustomerIdentity;

namespace IBeam.Management.Services.CustomerIdentity.Delegation;

/// <summary>Shared scoped verified actor metadata for optional Identity and registered-operation pipelines.</summary>
public sealed class ManagementDelegatedActorState : IManagementDelegatedActorContext
{
    public ManagementDelegatedActor? Current { get; private set; }
    internal void Set(ManagementDelegatedActor? actor) => Current = actor;
}
