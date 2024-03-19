using AutoMapper;
using IBeam.Models.API;
using IBeam.Models.Interfaces;
using IBeam.Services.Authorization;
using IBeam.Services.Interfaces;

namespace IBeam.Services.System
{
    public class BaseServices : IBaseServices
    {
        public IAccountContext AccountContext { get; set; }
        public IMapper Mapper { get; set; }
        public ISystemAuditService SystemAuditService { get; set; }
        public IServiceAuthorizationService SystemAuthorizationService { get; set; }
        //public IErrorLogService _errorLogService { get; set; }

        public BaseServices(IMapper mapper, ISystemAuditService systemAuditService, IServiceAuthorizationService systemAuthorizationService)
        {
            Mapper = mapper;
            SystemAuditService = systemAuditService;
            SystemAuthorizationService = systemAuthorizationService;
        }
    }
}
