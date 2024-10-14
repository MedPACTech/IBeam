using IBeam.DataModels;
using System.Collections.Generic;

namespace IBeam.Repositories.Interfaces
{
	public interface IAccountRepository : IBaseRepository<AccountDTO>
	{
		public AccountDTO GetByEmail(string email);
		public IEnumerable<AccountDTO> GetArchived();
	}
}
