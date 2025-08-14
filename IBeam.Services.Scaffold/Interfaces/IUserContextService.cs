using System;
using IBeam.Scaffolding.DataModels;
using IBeam.Scaffolding.Models.API;

namespace IBeam.Scaffolding.Services.Interfaces
{
	public interface IAccountContextService
	{
        IAccountContext FetchByAccount(Guid AccountId);
        void Save(IAccountContext AccountContext);
        void SaveBaseContext(AccountContextDTO AccountContext);
        AccountContextDTO GetBaseContext(Guid AccountId);
    }
}
