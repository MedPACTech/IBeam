using IBeam.Scaffolding.DataModels;
using System;
using System.Collections.Generic;
using IBeam.Repositories.Interfaces;
namespace IBeam.Scaffolding.Repositories.Interfaces
{
	public interface IDocumentRepository : IBaseRepository<DocumentDTO>
	{
		IEnumerable<DocumentDTO> GetByAssociatedId(Guid equipmentId);
		bool Archive(DocumentDTO image);
	}
}
