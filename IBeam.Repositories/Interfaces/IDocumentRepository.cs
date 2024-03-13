using IBeam.DataModels;
using IBeam.Models;
using System;
using System.Collections.Generic;

namespace IBeam.Repositories.Interfaces
{
	public interface IDocumentRepository : IRepository<DocumentDTO>
	{
		IEnumerable<DocumentDTO> GetByAssociatedId(Guid equipmentId);
		bool Archive(Document image);
	}
}
