using AutoMapper;
using IBeam.Repositories;
using IBeam.DataModels;
using System;
using System.Collections.Generic;
using IBeam.Models;
using IBeam.Repositories.Interfaces;
using IBeam.Services.Interfaces;

namespace IBeam.Services.Authorization
{

	public class AccountGroupMemberService : IAccountGroupMemberService
	{
        private readonly IMapper _mapper;
        private readonly IAccountGroupMemberRepository _AccountGroupMemberRepository;

        public AccountGroupMemberService(IMapper mapper, IAccountGroupMemberRepository AccountGroupMemberRepository)
        {
            _mapper = mapper;
            _AccountGroupMemberRepository = AccountGroupMemberRepository;
        }

        public IAccountGroupMember Fetch(Guid id)
        {

            if (id == Guid.Empty)
                return new AccountGroupMember();
            else
            {
                var AccountGroupMemberDTO = _AccountGroupMemberRepository.GetById(id);
                return _mapper.Map<AccountGroupMember>(AccountGroupMemberDTO);
            }
        }

        public IEnumerable<IAccountGroupMember> FetchByAccount(Guid AccountId)
        {
            var AccountGroupMemberDTOs = _AccountGroupMemberRepository.GetByAccountId(AccountId);
            return _mapper.Map<IEnumerable<AccountGroupMember>>(AccountGroupMemberDTOs);
        }

        public IEnumerable<IAccountGroupMember> FetchByAccountGroup(Guid AccountGroupId)
        {
            var AccountGroupMemberDTOs = _AccountGroupMemberRepository.GetByAccountGroupId(AccountGroupId);
            return _mapper.Map<IEnumerable<AccountGroupMember>>(AccountGroupMemberDTOs);
        }

        public void RemoveAccountFromGroup(Guid AccountId, Guid AccountGroupId)
        {
            _AccountGroupMemberRepository.RemoveAccountFromGroup(AccountId, AccountGroupId);
        }

        public void Save(IAccountGroupMember AccountGroupMember)
        {
            if (AccountGroupMember.Id == Guid.Empty)
                AccountGroupMember.Id = Guid.NewGuid();

            var AccountGroupMemberDTO = _mapper.Map<AccountGroupMemberDTO>(AccountGroupMember);
            _AccountGroupMemberRepository.Save(AccountGroupMemberDTO);
        }

	}
}
