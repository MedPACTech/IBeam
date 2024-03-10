using System;

namespace IBeam.Models.Interfaces
{
	public interface IApplication
	{

		 Guid Id { get; set; }
		 String Name { get; set; }
		 string Url { get; set; }

	}
}
