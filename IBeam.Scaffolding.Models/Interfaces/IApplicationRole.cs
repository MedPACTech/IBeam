using System;

namespace IBeam.Scaffolding.Models
{
	public interface IApplicationRole
	{

		 Guid Id { get; set; }
		 Guid ApplicationId { get; set; }
		 string RoleName { get; set; }
		 string Descripition { get; set; }
		 bool IsActive { get; set; }

	}
}
