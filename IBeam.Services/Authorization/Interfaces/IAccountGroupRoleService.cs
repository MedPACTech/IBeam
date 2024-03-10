using AutoMapper;
using IBeam.Repositories;
using IBeam.DataModels;
using System;
using System.Collections.Generic;
using IBeam.Models;

namespace IBeam.Services.Authorization
{
	public interface IAccountGroupRoleService
	{
        IAccountGroupRole Fetch(Guid id);
        IEnumerable<IAccountGroupRole> FetchByAccountGroup(Guid AccountGroupId);
        IEnumerable<IAccountGroupRole> FetchByAccountGroups(IEnumerable<Guid> AccountGroupIds);
        void Save(IAccountGroupRole AccountGroupRole);

	}
}
