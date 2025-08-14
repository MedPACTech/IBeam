using AutoMapper;
using IBeam.Repositories;
using IBeam.Scaffolding.DataModels;
using System;
using System.Collections.Generic;
using IBeam.Repositories.Interfaces;
using IBeam.Scaffolding.Models.Interfaces;
using IBeam.Scaffolding.Services.Interfaces;
using IBeam.Scaffolding.Repositories.Interfaces;
using IBeam.Scaffolding.Models;

namespace IBeam.Scaffolding.Services.Authorization
{

	public class AccountGroupRoleService : IAccountGroupRoleService
	{
        private readonly IMapper _mapper;
        private readonly IAccountGroupRoleRepository _AccountGroupRoleRepository;

        public AccountGroupRoleService(IMapper mapper, IAccountGroupRoleRepository AccountGroupRoleRepository)
        {
            _mapper = mapper;
            _AccountGroupRoleRepository = AccountGroupRoleRepository;
        }

        public IAccountGroupRole Fetch(Guid id)
        {

            if (id == Guid.Empty)
                return new AccountGroupRole();
            else
            {
                var AccountGroupRoleDTO = _AccountGroupRoleRepository.GetById(id);
                return _mapper.Map<AccountGroupRole>(AccountGroupRoleDTO);
            }
        }

        public IEnumerable<IAccountGroupRole> FetchByAccountGroup(Guid AccountGroupId)
        {
            var AccountGroupRoleDTOs = _AccountGroupRoleRepository.GetByAccountGroupId(AccountGroupId);
            return _mapper.Map<IEnumerable<AccountGroupRole>>(AccountGroupRoleDTOs);
        }

        public IEnumerable<IAccountGroupRole> FetchByAccountGroups(IEnumerable<Guid> AccountGroupIds)
        {
            var AccountGroupRoleDTOs = _AccountGroupRoleRepository.GetByAccountGroupIds(AccountGroupIds);
            return _mapper.Map<IEnumerable<AccountGroupRole>>(AccountGroupRoleDTOs);
        }

        public void Save(IAccountGroupRole AccountGroupRole)
        {
            if (AccountGroupRole.Id == Guid.Empty)
                AccountGroupRole.Id = Guid.NewGuid();

            var AccountGroupRoleDTO = _mapper.Map<AccountGroupRoleDTO>(AccountGroupRole);
            _AccountGroupRoleRepository.Save(AccountGroupRoleDTO);
        }
	}
}
