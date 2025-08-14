using IBeam.DataModels;
using IBeam.Scaffolding.DataModels;
using IBeam.Scaffolding.Models;
using IBeam.Scaffolding.Models.API;

namespace IBeam.Mappings
{
    public class MappingProfiles : AutoMapper.Profile
    {
        public MappingProfiles()
        {
            Add<ApplicationRole, ApplicationRoleDTO>();
            Add<Account, AccountDTO>();
            Add<AccountDevice, AccountDeviceDTO>();
            Add<AccountGroup, AccountGroupDTO>();
            Add<AccountGroupMember, AccountGroupMemberDTO>();
            Add<AccountGroupRole, AccountGroupRoleDTO>();
            Add<AccountContext, AccountContextDTO>();
            Add<RefreshToken, RefreshTokenDTO>();
            Add<License, LicenseDTO>();
            Add<Application, ApplicationDTO>();
            Add<ApplicationAccount, ApplicationAccountDTO>();
            Add<SystemAudit, SystemAuditDTO>();
            Add<Notification, NotificationDTO>();
            Add<ApplicationRoleAccess, ApplicationRoleAccessDTO>();
            Add<ServiceAuthorization, ApplicationRoleAccessDTO>();
            Add<Document, DocumentDTO>();
            Add<Notification, NotificationDTO>();
        }

        private void Add<A, B>()
        {
            CreateMap<A, B>().ReverseMap();
        }
    }
}
