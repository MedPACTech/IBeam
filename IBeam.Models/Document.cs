using Microsoft.AspNetCore.Http;

namespace IBeam.Models
{
	public class Document
	{
		public Guid Id { get; set; }
		public string Name { get; set; }
		public string ContentType { get; set; }
		public byte[] Content { get; set; }
		public string AdditionalData { get; set; }
		public bool IsArchived { get; set; }

		public Document()
        {

        }

		public Document(IFormFile image, string additionalData)
        {
			Id = Guid.NewGuid();
			Name = image.FileName;
			ContentType = image.ContentType;
			Content = FileToBytes(image);
			AdditionalData = additionalData;
        }

		public byte[] FileToBytes(IFormFile file)
		{
			using (var ms = new MemoryStream())
			{
				file.CopyTo(ms);
				var fileBytes = ms.ToArray();
				return fileBytes;
			}
		}
	}

	public class AdditionalData
    {
		public Guid AssociatedId { get; set; }
		public string Date { get; set; }
    }
}
