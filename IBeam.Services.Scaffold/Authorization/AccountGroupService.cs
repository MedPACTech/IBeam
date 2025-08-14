using AutoMapper;
using IBeam.Repositories;
using IBeam.Scaffolding.DataModels;
using System;
using System.Collections.Generic;
using IBeam.Scaffolding.Models.Interfaces;
using IBeam.Scaffolding.Services.Interfaces;
using IBeam.Repositories.Interfaces;
using System.Linq;
using IBeam.Scaffolding.Repositories.Interfaces;
using IBeam.Scaffolding.Models;

namespace IBeam.Scaffolding.Services.Authorization
{

	public class AccountGroupService : IAccountGroupService
	{
        private readonly IMapper _mapper;
        private readonly IAccountGroupRepository _AccountGroupRepository;
        private readonly IAccountGroupMemberService _AccountGroupMemberService;

        public AccountGroupService(IMapper mapper, IAccountGroupRepository AccountGroupRepository, IAccountGroupMemberService AccountGroupMemberService)
        {
            _mapper = mapper;
            _AccountGroupRepository = AccountGroupRepository;
            _AccountGroupMemberService = AccountGroupMemberService;
        }

        public IAccountGroup Fetch(Guid id)
        {

            if (id == Guid.Empty)
                return new AccountGroup();
            else
            {
                var AccountGroupDTO = _AccountGroupRepository.GetById(id);
                return _mapper.Map<AccountGroup>(AccountGroupDTO);
            }
        }

        public IEnumerable<IAccountGroup> FetchAll()
        {
            var AccountGroupDTOs = _AccountGroupRepository.GetAll();
            return _mapper.Map<IEnumerable<AccountGroup>>(AccountGroupDTOs);
        }

        public IEnumerable<IAccountGroup> FetchByIds(List<Guid> ids)
        {
          var AccountGroupDTOs = _AccountGroupRepository.GetByIds(ids);
          return _mapper.Map<IEnumerable<AccountGroup>>(AccountGroupDTOs);
        }

        public IEnumerable<IAccountGroup> FetchByAccount(Guid AccountId)
        {
            var AccountGroupIds = _AccountGroupMemberService.FetchByAccount(AccountId).Select(x=> x.AccountGroupId).ToList();
            return FetchByIds(AccountGroupIds);
        }

        public void Save(IAccountGroup AccountGroup)
        {
            if (AccountGroup.Id == Guid.Empty)
                AccountGroup.Id = Guid.NewGuid();

            var AccountGroupDTO = _mapper.Map<AccountGroupDTO>(AccountGroup);
            _AccountGroupRepository.Save(AccountGroupDTO);
        }

        public void Delete(Guid id)
        {
            _AccountGroupRepository.DeleteById(id);
        }

    }
}
