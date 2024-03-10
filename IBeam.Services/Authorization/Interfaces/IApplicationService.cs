using System;
using IBeam.Models.Interfaces;

namespace IBeam.Services.Authorization
{
	public interface IApplicationService
	{
        IApplication Fetch(Guid id);
        void Save(IApplication application);

	}
}
