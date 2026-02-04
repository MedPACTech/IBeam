using IBeam.DataModels.System;
using ServiceStack.DataAnnotations;
using System;

namespace IBeam.Scaffolding.DataModels
{

    [Serializable]
	[Alias("Applications")]
	public class ApplicationDTO : IEntity
	{

		public Guid Id { get; set; }
		public String Name { get; set; }
		public string Url { get; set; }
        public bool IsDeleted { get; set; }
    }
}
