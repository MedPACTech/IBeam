using IBeam.DataModels;
using IBeam.Repositories.Interfaces;
using ServiceStack.OrmLite;
using System;
using System.Collections.Generic;
using System.Data;

namespace IBeam.Repositories.Interfaces
{
    public interface IAccountRoleRepository : IRepository<AccountRoleDTO>
    {
        IEnumerable<AccountRoleDTO> GetByAccountId(Guid AccountId);
    }
}
