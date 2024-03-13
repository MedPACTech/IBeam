using ServiceStack.DataAnnotations;
using System;

namespace IBeam.DataModels
{

	[Serializable]
	[Alias("Applications")]
	public class ApplicationDTO : IDTO
	{

		public Guid Id { get; set; }
		public String Name { get; set; }
		public string Url { get; set; }
    }
}
