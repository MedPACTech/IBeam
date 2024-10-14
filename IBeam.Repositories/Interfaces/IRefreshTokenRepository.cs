using IBeam.DataModels;

namespace IBeam.Repositories.Interfaces
{
    public interface IRefreshTokenRepository : IBaseRepository<RefreshTokenDTO>
    {
        public RefreshTokenDTO GetByToken(string token);
    }
}
