using IBeam.Scaffolding.DataModels;
using IBeam.Scaffolding.Repositories.Interfaces;
using ServiceStack.OrmLite;
using System;
using System.Collections.Generic;
using System.Data;
using IBeam.Repositories.Interfaces;
namespace IBeam.Scaffolding.Repositories.Interfaces
{
    public interface IAccountContextRepository : IBaseRepository<AccountContextDTO>
    {
        AccountContextDTO GetByAccountId(Guid AccountId);
    }
}
