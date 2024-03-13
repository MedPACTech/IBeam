using IBeam.DataModels;
using System.Collections.Generic;

namespace IBeam.Repositories.Interfaces
{
	public interface IAccountRepository : IRepository<AccountDTO>
	{
		public AccountDTO GetByEmail(string email);
		public IEnumerable<AccountDTO> GetArchived();
	}
}
