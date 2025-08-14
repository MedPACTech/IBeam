using System;
using IBeam.Scaffolding.Models.Interfaces;
using IBeam.Scaffolding.Models.Interfaces;

namespace IBeam.Scaffolding.Services.Authorization
{
	public interface IApplicationAccountService
	{
        IApplicationAccount Fetch(Guid id);
        void Save(IApplicationAccount applicationAccount);

	}
}
