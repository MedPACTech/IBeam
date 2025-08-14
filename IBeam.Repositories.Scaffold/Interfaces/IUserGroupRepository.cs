using IBeam.Scaffolding.DataModels;
using IBeam.Scaffolding.Repositories.Interfaces;
using ServiceStack.OrmLite;
using System;
using System.Collections.Generic;
using System.Data;
using IBeam.Repositories.Interfaces;
namespace IBeam.Scaffolding.Repositories.Interfaces
{
    public interface IAccountGroupRepository : IBaseRepository<AccountGroupDTO>
    {
        //object GetByAccountId(Guid AccountId);
    }
}
