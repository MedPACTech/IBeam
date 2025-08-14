using AutoMapper;
using IBeam.Repositories;
using IBeam.Scaffolding.DataModels;
using System;
using System.Collections.Generic;
using IBeam.Scaffolding.Models.Interfaces;
using IBeam.Scaffolding.Models;

namespace IBeam.Scaffolding.Services.Authorization
{
	public interface IApplicationRoleService
	{
        IApplicationRole Fetch(Guid id);
        IEnumerable<IApplicationRole> FetchByApplicationId(Guid applicationId);
        void Save(IApplicationRole applicationRole);
    }
}
