using System;
using IBeam.DataModels.System;
using ServiceStack.DataAnnotations;

namespace IBeam.DataModels
{
    [Serializable]
	[Alias("Documents")]
	public class DocumentDTO : IDTO, IDTOArchive
	{
		public Guid Id { get; set; }
		public string Name { get; set; }
		public string ContentType { get; set; }
		public byte[] Content { get; set; }
		public string AdditionalData { get; set; }
		public bool IsArchived { get; set; }
        public bool IsDeleted { get; set; }
    }
}
