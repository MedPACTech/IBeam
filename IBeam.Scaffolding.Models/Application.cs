using System;
using IBeam.Scaffolding.Models.Interfaces;

namespace IBeam.Scaffolding.Models
{	public class Application : IApplication
	{

		public Guid Id { get; set; }
		public String Name { get; set; }
		public string Url { get; set; }

	}
}
