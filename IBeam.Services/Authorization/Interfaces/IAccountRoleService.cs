using AutoMapper;
using IBeam.Repositories;
using IBeam.DataModels;
using System;
using System.Collections.Generic;
using IBeam.Models;

namespace IBeam.Services.Authorization
{
	public interface IAccountRoleService
	{
        IAccountRole Fetch(Guid id);
		IEnumerable<IAccountRole> FetchByAccount(Guid AccountId);  
		void Save(IAccountRole AccountRole);

	}
}
