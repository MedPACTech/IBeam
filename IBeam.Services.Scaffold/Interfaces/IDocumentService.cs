using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.Http;
using IBeam.Scaffolding.Models.Interfaces;
using IBeam.Scaffolding.Models;

namespace IBeam.Scaffolding.Services.Interfaces
{
	public interface IDocumentService
	{
        Guid Save(Document image);
		IEnumerable<Document> GetByAssociatedId(Guid equipmentId);
		bool Archive(Document image);
		Document GetDocument(Guid id);
		MemoryStream GetDocumentAsStream(Guid id);
		Guid SaveGeneratedPdf(string name, byte[] data);
		void CombinePDFs(Guid certId, Guid[] ids, string name, byte[] certContent);
		IEnumerable<Document> GetByIds(List<Guid> ids);
	}
}
