using AutoMapper;
using IBeam.Repositories;
using IBeam.Scaffolding.DataModels;
using IBeam.Scaffolding.Services.Interfaces;
using IBeam.Scaffolding.Repositories.Interfaces;
using IBeam.Scaffolding.Models.API;

namespace IBeam.Scaffolding.Services
{

	public class AccountContextService : IAccountContextService
	{
        private readonly IMapper _mapper;
        private readonly IAccountContextRepository _AccountContextRepository;

        public AccountContextService(IMapper mapper, IAccountContextRepository AccountContextRepository)
        {
            _mapper = mapper;
            _AccountContextRepository = AccountContextRepository;
        }

        public IAccountContext Fetch(Guid id)
        {

            //if (id == Guid.Empty)
                return new AccountContext();
            //else
            //{
            //    var AccountContextDTO = _AccountContextRepository.GetById(id);
            //    return _mapper.Map<AccountContext>(AccountContextDTO);
            //}
        }

        public IAccountContext FetchByAccount(Guid AccountId)
        {
            if (AccountId == Guid.Empty)
                return new AccountContext();
            else
            {
                var AccountContextDTO = _AccountContextRepository.GetByAccountId(AccountId);
                return _mapper.Map<AccountContext>(AccountContextDTO);
            }
        }

        public void Save(IAccountContext AccountContext)
        {
            if (AccountContext.Id == Guid.Empty)
                AccountContext.Id = Guid.NewGuid();

            var AccountContextDTO = _mapper.Map<AccountContextDTO>(AccountContext);
            _AccountContextRepository.Save(AccountContextDTO);
        }

        public void SaveBaseContext(AccountContextDTO AccountContext)
        {
            if (AccountContext.Id == Guid.Empty)
                AccountContext.Id = Guid.NewGuid();

            _AccountContextRepository.Save(AccountContext);
        }

        public AccountContextDTO GetBaseContext(Guid AccountId)
        {
            var AccountContextDTO = _AccountContextRepository.GetByAccountId(AccountId);
            return AccountContextDTO;
        }
    }
}
