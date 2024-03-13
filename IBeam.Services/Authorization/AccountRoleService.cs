using AutoMapper;
using IBeam.Repositories;
using IBeam.DataModels;
using System;
using System.Collections.Generic;
using IBeam.Models;
using IBeam.Services.Interfaces;
using IBeam.Repositories.Interfaces;

namespace IBeam.Services.Authorization
{

	public class AccountRoleService : IAccountRoleService
	{
        private readonly IMapper _mapper;
        private readonly IAccountRoleRepository _AccountRoleRepository;

        public AccountRoleService(IMapper mapper, IAccountRoleRepository AccountRoleRepository)
        {
            _mapper = mapper;
            _AccountRoleRepository = AccountRoleRepository;
        }

        public IAccountRole Fetch(Guid id)
        {

            if (id == Guid.Empty)
                return new AccountRole();
            else
            {
                var AccountRoleDTO = _AccountRoleRepository.GetById(id);
                return _mapper.Map<AccountRole>(AccountRoleDTO);
            }
        }

        public IEnumerable<IAccountRole> FetchByAccount(Guid AccountId)
        {
            var AccountRoleDTOs = _AccountRoleRepository.GetByAccountId(AccountId);
            return _mapper.Map<IEnumerable<AccountRole>>(AccountRoleDTOs);
        }

        public void Save(IAccountRole AccountRole)
        {
            if (AccountRole.Id == Guid.Empty)
                AccountRole.Id = Guid.NewGuid();

            var AccountRoleDTO = _mapper.Map<AccountRoleDTO>(AccountRole);
            _AccountRoleRepository.Save(AccountRoleDTO);
        }

	}
}
