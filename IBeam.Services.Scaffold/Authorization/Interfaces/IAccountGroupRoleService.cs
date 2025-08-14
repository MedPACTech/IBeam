using AutoMapper;
using IBeam.Repositories;
using IBeam.Scaffolding.DataModels;
using System;
using System.Collections.Generic;
using IBeam.Scaffolding.Models.Interfaces;
using IBeam.Scaffolding.Models;

namespace IBeam.Scaffolding.Services.Authorization
{
	public interface IAccountGroupRoleService
	{
        IAccountGroupRole Fetch(Guid id);
        IEnumerable<IAccountGroupRole> FetchByAccountGroup(Guid AccountGroupId);
        IEnumerable<IAccountGroupRole> FetchByAccountGroups(IEnumerable<Guid> AccountGroupIds);
        void Save(IAccountGroupRole AccountGroupRole);

	}
}
