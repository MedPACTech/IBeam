using AutoMapper;
using IBeam.Repositories;
using IBeam.DataModels;
using System;
using System.Collections.Generic;

using IBeam.Models;

namespace IBeam.Services.Authorization
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
