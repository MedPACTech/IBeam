using IBeam.Scaffolding.DataModels;
using IBeam.Repositories.Interfaces;
namespace IBeam.Scaffolding.Repositories.Interfaces
{
    public interface IRefreshTokenRepository : IBaseRepository<RefreshTokenDTO>
    {
        public RefreshTokenDTO GetByToken(string token);
    }
}
