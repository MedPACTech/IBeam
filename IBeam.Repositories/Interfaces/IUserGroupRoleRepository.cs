using IBeam.DataModels;
using IBeam.Repositories.Interfaces;
using ServiceStack.OrmLite;
using System;
using System.Collections.Generic;
using System.Data;

namespace IBeam.Repositories.Interfaces
{
    public interface IAccountGroupRoleRepository : IBaseRepository<AccountGroupRoleDTO>
    {
        IEnumerable<AccountGroupRoleDTO> GetByAccountGroupId(Guid AccountGroupId);
        IEnumerable<AccountGroupRoleDTO> GetByAccountGroupIds(IEnumerable<Guid> AccountGroupIds);
    }
}
