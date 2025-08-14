using IBeam.Scaffolding.DataModels;
using IBeam.Repositories.Interfaces;
using IBeam.Scaffolding.Repositories.Interfaces;
using ServiceStack.OrmLite;
using System;
using System.Collections.Generic;
using System.Data;

namespace IBeam.Scaffolding.Repositories.Interfaces
{
    public interface IApplicationRoleAccessRepository : IBaseRepository<ApplicationRoleAccessDTO>
    {
        IEnumerable<ApplicationRoleAccessDTO> GetByApplicationRoleIds(IEnumerable<Guid> applicationRoleIds);
    }
}
