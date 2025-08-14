using AutoMapper;
using IBeam.Repositories;
using IBeam.Scaffolding.DataModels;
using System;
using System.Collections.Generic;
using IBeam.Scaffolding.Models.Interfaces;
using IBeam.Scaffolding.Models;

namespace IBeam.Scaffolding.Services.Authorization
{
	public interface IAccountRoleService
	{
        IAccountRole Fetch(Guid id);
		IEnumerable<IAccountRole> FetchByAccount(Guid AccountId);  
		void Save(IAccountRole AccountRole);

	}
}
