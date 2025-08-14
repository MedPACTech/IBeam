using AutoMapper;
using IBeam.Repositories;
using IBeam.Scaffolding.DataModels;
using System;
using System.Collections.Generic;

using IBeam.Scaffolding.Models.Interfaces;
using IBeam.Scaffolding.Models;

namespace IBeam.Scaffolding.Services.Authorization
{
	public interface IAccountGroupService
	{
        IAccountGroup Fetch(Guid id);
        void Save(IAccountGroup AccountGroup);
        IEnumerable<IAccountGroup> FetchByAccount(Guid AccountId);
        IEnumerable<IAccountGroup> FetchAll();
        void Delete(Guid id);
    }
}
