using System;

namespace IBeam.Scaffolding.Models.Interfaces
{
	public interface IApplication
	{

		 Guid Id { get; set; }
		 String Name { get; set; }
		 string Url { get; set; }

	}
}
