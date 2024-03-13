using System;
using ServiceStack.DataAnnotations;

namespace IBeam.DataModels
{
	[Serializable]
	[Alias("Documents")]
	public class DocumentDTO : IDTOArchive
	{
		public Guid Id { get; set; }
		public string Name { get; set; }
		public string ContentType { get; set; }
		public byte[] Content { get; set; }
		public string AdditionalData { get; set; }
		public bool IsArchived { get; set; }
	}
}
