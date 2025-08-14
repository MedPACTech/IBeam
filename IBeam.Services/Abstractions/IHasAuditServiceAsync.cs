namespace IBeam.Services.Abstractions
{
    internal interface IHasAuditServiceAsync : IBaseServices
    {
        IAuditServiceAsync AuditServiceAsync { get; }
    }
}