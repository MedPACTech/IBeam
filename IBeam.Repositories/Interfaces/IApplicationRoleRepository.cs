using IBeam.DataModels;
using IBeam.Repositories.Interfaces;
using ServiceStack.OrmLite;
using System;
using System.Collections.Generic;
using System.Data;

namespace IBeam.Repositories.Interfaces
{
    public interface IApplicationRoleRepository : IBaseRepository<ApplicationRoleDTO>
    {
        IEnumerable<ApplicationRoleDTO> GetByApplicationId(Guid applicationId);
    }
}
