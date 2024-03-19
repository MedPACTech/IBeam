using AutoMapper;
using IBeam.Models.API;
using IBeam.Models.Interfaces;
using IBeam.Services.Authorization;
using IBeam.Services.Interfaces;

namespace IBeam.Services.System
{
    public interface IBaseServices
    {
       // IHttpContextAccessor HttpContextAccessor { get; set; }
        IAccountContext AccountContext { get; set; }
        IMapper Mapper { get; set; }
        ISystemAuditService SystemAuditService { get; set; }
        IServiceAuthorizationService SystemAuthorizationService { get; set; }
        //IErrorLogService _errorLogService { get; set; }
    }
}