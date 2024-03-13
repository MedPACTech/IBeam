using Microsoft.Extensions.Caching.Memory;
using IBeam.DataModels;
using IBeam.Repositories.Interfaces;
using IBeam.API.Utilities;
using Microsoft.Extensions.Options;

namespace IBeam.Repositories
{
	public class ApplicationRepository : BaseRepository<ApplicationDTO>, IApplicationRepository
	{
		public ApplicationRepository(IOptions<AppSettings> appSettings, IMemoryCache memoryCache) : base(appSettings, memoryCache)
		{
		}
	}
}
