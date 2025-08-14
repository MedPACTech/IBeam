using System;
using IBeam.Scaffolding.Models.Interfaces;

namespace IBeam.Scaffolding.Services.Authorization
{
	public interface IApplicationService
	{
        IApplication Fetch(Guid id);
        void Save(IApplication application);

	}
}
