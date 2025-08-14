using IBeam.Scaffolding.DataModels;
using System.Collections.Generic;
using IBeam.Repositories.Interfaces;
namespace IBeam.Scaffolding.Repositories.Interfaces
{
	public interface IAccountRepository : IBaseRepository<AccountDTO>
	{
		public AccountDTO GetByEmail(string email);
		public IEnumerable<AccountDTO> GetArchived();
	}
}
