using AutoMapper;
using IBeam.Repositories;
using IBeam.Scaffolding.DataModels;
using System;
using System.Collections.Generic;
using IBeam.Scaffolding.Models.Interfaces;
using IBeam.Scaffolding.Models;

namespace IBeam.Scaffolding.Services.Authorization
{
	public interface IApplicationRoleAccessService
	{
        IApplicationRoleAccess Fetch(Guid id);
        void Save(IApplicationRoleAccess applicationRoleAccess);
        IEnumerable<IApplicationRoleAccess> Fetch();
        IEnumerable<IApplicationRoleAccess> FetchByApplication(Guid applicationId);  
    }
}
