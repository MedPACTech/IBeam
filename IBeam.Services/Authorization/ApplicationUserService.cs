using System;
using AutoMapper;
using IBeam.DataModels;
using IBeam.Models;
using IBeam.Models.Interfaces;
using IBeam.Repositories.Interfaces;
using IBeam.Services.Interfaces;

namespace IBeam.Services.Authorization
{

	public class ApplicationAccountService : IApplicationAccountService
	{
        private readonly IMapper _mapper;
        private readonly IApplicationAccountRepository _applicationAccountRepository;

        public ApplicationAccountService(IMapper mapper, IApplicationAccountRepository applicationAccountRepository)
        {
            _mapper = mapper;
            _applicationAccountRepository = applicationAccountRepository;
        }

        public IApplicationAccount Fetch(Guid id)
        {

            if (id == Guid.Empty)
                return new ApplicationAccount();
            else
            {
                var applicationAccountDTO = _applicationAccountRepository.GetById(id);
                return _mapper.Map<ApplicationAccount>(applicationAccountDTO);
            }
        }

        public void Save(IApplicationAccount applicationAccount)
        {
            if (applicationAccount.Id == Guid.Empty)
                applicationAccount.Id = Guid.NewGuid();

            var applicationAccountDTO = _mapper.Map<ApplicationAccountDTO>(applicationAccount);
            _applicationAccountRepository.Save(applicationAccountDTO);
        }

	}
}
