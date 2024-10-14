using IBeam.DataModels;
using IBeam.Repositories.Interfaces;
using ServiceStack.OrmLite;
using System;
using System.Collections.Generic;
using System.Data;

namespace IBeam.Repositories.Interfaces
{
    public interface IAccountGroupMemberRepository : IBaseRepository<AccountGroupMemberDTO>
    {
        IEnumerable<AccountGroupMemberDTO> GetByAccountId(Guid AccountId);
        IEnumerable<AccountGroupMemberDTO> GetByAccountGroupId(Guid AccountGroupId);
        void RemoveAccountFromGroup(Guid AccountId, Guid AccountGroupId);
    }
}
