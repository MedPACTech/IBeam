using System;
using IBeam.Models.Interfaces;

namespace IBeam.Models
{	public class Application : IApplication
	{

		public Guid Id { get; set; }
		public String Name { get; set; }
		public string Url { get; set; }

	}
}
