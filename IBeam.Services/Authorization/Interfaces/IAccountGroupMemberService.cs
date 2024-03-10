using AutoMapper;
using IBeam.Repositories;
using IBeam.DataModels;
using System;
using System.Collections.Generic;
using IBeam.Models;

namespace IBeam.Services.Authorization
{
	public interface IAccountGroupMemberService
	{
        IAccountGroupMember Fetch(Guid id);
        IEnumerable<IAccountGroupMember> FetchByAccount(Guid AccountId);
        void Save(IAccountGroupMember AccountGroupMember);
        IEnumerable<IAccountGroupMember> FetchByAccountGroup(Guid AccountGroupId);
        void RemoveAccountFromGroup(Guid AccountId, Guid AccountGroupId);
	}
}
