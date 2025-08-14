using IBeam.Scaffolding.DataModels;
using IBeam.Scaffolding.Repositories.Interfaces;
using ServiceStack.OrmLite;
using System;
using System.Collections.Generic;
using System.Data;
using IBeam.Repositories.Interfaces;
namespace IBeam.Scaffolding.Repositories.Interfaces
{
    public interface IAccountGroupMemberRepository : IBaseRepository<AccountGroupMemberDTO>
    {
        IEnumerable<AccountGroupMemberDTO> GetByAccountId(Guid AccountId);
        IEnumerable<AccountGroupMemberDTO> GetByAccountGroupId(Guid AccountGroupId);
        void RemoveAccountFromGroup(Guid AccountId, Guid AccountGroupId);
    }
}
