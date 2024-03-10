using AutoMapper;
using IBeam.Repositories;
using IBeam.DataModels;
using System;
using System.Collections.Generic;
using IBeam.Models;

namespace IBeam.Services.Authorization
{
	public interface IApplicationRoleAccessService
	{
        IApplicationRoleAccess Fetch(Guid id);
        void Save(IApplicationRoleAccess applicationRoleAccess);
        IEnumerable<IApplicationRoleAccess> Fetch();
        IEnumerable<IApplicationRoleAccess> FetchByApplication(Guid applicationId);  
    }
}
