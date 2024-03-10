using IBeam.DataModels;
using IBeam.Repositories.Interfaces;
using ServiceStack.OrmLite;
using System;
using System.Collections.Generic;
using System.Data;

namespace IBeam.Repositories.Interfaces
{
    public interface IApplicationRoleAccessRepository : IRepository<ApplicationRoleAccessDTO>
    {
        IEnumerable<ApplicationRoleAccessDTO> GetByApplicationRoleIds(IEnumerable<Guid> applicationRoleIds);
    }
}
