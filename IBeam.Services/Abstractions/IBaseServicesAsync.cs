// IBeam.Services.Abstractions/IBaseServices.cs
using AutoMapper;

namespace IBeam.Services.Abstractions
{
    public interface IBaseServicesAsync
    {
        //IAccountContext AccountContext { get; }   // your existing context interface
        IMapper Mapper { get; }
        IAuditServiceAsync AuditService { get; }

        //perhaps remove AccoutnContext, but we could add permission services here
    }
}
