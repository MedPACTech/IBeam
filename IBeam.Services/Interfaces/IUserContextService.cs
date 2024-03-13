using System;
using IBeam.DataModels;
using IBeam.Models.API;

namespace IBeam.Services.Interfaces
{
	public interface IAccountContextService
	{
        IAccountContext FetchByAccount(Guid AccountId);
        void Save(IAccountContext AccountContext);
        void SaveBaseContext(AccountContextDTO AccountContext);
        AccountContextDTO GetBaseContext(Guid AccountId);
    }
}
