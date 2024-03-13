using IBeam.DataModels;

namespace IBeam.Repositories.Interfaces
{
    public interface IRefreshTokenRepository : IRepository<RefreshTokenDTO>
    {
        public RefreshTokenDTO GetByToken(string token);
    }
}
