using AutoMapper;
using IBeam.Repositories;
using IBeam.Scaffolding.DataModels;
using System;
using System.Collections.Generic;
using IBeam.Scaffolding.Models.Interfaces;
using IBeam.Scaffolding.Models;

namespace IBeam.Scaffolding.Services.Authorization
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
