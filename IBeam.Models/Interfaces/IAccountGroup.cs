using System;

namespace IBeam.Models
{
	public interface IAccountGroup
	{

		 Guid Id { get; set; }
		 Guid ApplicationId { get; set; }
		 string Name { get; set; }
		 string Description { get; set; }
		 bool IsActive { get; set; }

	}
}
