using Microsoft.Extensions.Caching.Memory;
using IBeam.DataModels;
using IBeam.Repositories.Interfaces;
using IBeam.Utilities;
using Microsoft.Extensions.Options;

namespace IBeam.Repositories
{
	public class ApplicationAccountRepository : BaseRepository<ApplicationAccountDTO>, IApplicationAccountRepository
	{
		public ApplicationAccountRepository(IOptions<BaseAppSettings> appSettings, IMemoryCache memoryCache) : base(appSettings, memoryCache)
		{

        }
	}
}
