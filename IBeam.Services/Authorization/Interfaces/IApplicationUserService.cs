using System;
using IBeam.Models.Interfaces;

namespace IBeam.Services.Authorization
{
	public interface IApplicationAccountService
	{
        IApplicationAccount Fetch(Guid id);
        void Save(IApplicationAccount applicationAccount);

	}
}
