// IBeam.Services/BaseServices.cs
using AutoMapper;
using IBeam.Services.Abstractions;

namespace IBeam.Services
{
    public sealed class BaseServices : IBaseServices
    {
        public IMapper Mapper { get; }
        public IAuditService AuditService { get; }

        public BaseServices(
            IMapper mapper,
            IAuditService auditService)
        {
            Mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            AuditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
        }
    }
}
