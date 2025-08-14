using IBeam.Scaffolding.DataModels;
using IBeam.Repositories.Interfaces;
using IBeam.Scaffolding.Repositories.Interfaces;
using ServiceStack.OrmLite;
using System;
using System.Collections.Generic;
using System.Data;

namespace IBeam.Scaffolding.Repositories.Interfaces
{
    public interface IApplicationRoleRepository : IBaseRepository<ApplicationRoleDTO>
    {
        IEnumerable<ApplicationRoleDTO> GetByApplicationId(Guid applicationId);
    }
}
