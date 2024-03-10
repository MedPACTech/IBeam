using AutoMapper;
using IBeam.Repositories;
using IBeam.DataModels;
using System;
using System.Collections.Generic;
using IBeam.Models;

namespace IBeam.Services.Authorization
{
	public interface IApplicationRoleService
	{
        IApplicationRole Fetch(Guid id);
        IEnumerable<IApplicationRole> FetchByApplicationId(Guid applicationId);
        void Save(IApplicationRole applicationRole);
    }
}
