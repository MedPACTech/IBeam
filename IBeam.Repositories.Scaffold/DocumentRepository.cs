using IBeam.Scaffolding.DataModels;
using IBeam.Scaffolding.Repositories.Interfaces;
using IBeam.Utilities;
using Microsoft.Extensions.Caching.Memory;
using ServiceStack.OrmLite;
using System.Data;
using Microsoft.Extensions.Options;
using IBeam.DataModels.System;
using IBeam.Repositories;

namespace IBeam.Scaffolding.Repositories
{
    public class DocumentRepository : BaseRepository<DocumentDTO>, IDocumentRepository
    {
        public DocumentRepository(TenantContext tenantContext, IOptions<BaseAppSettings> appSettings, IMemoryCache memoryCache) : base(tenantContext, appSettings, memoryCache) { }

        public IEnumerable<DocumentDTO> GetByAssociatedId(Guid id)
        {
            try
            {
                // get associated Id from additionalData and return matches
                using IDbConnection db = _dataFactory.OpenDbConnection();
                return db.Select<DocumentDTO>(x => x.AdditionalData.Contains(id.ToString()) && !x.IsArchived);
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "GetByAssociatedId");
            }
        }

        public bool Archive(DocumentDTO Document)
        {
            try
            {
                using IDbConnection db = _dataFactory.OpenDbConnection();
                var obj = db.Select<DocumentDTO>(x => x.Id == Document.Id).FirstOrDefault();
                obj.IsArchived = true;
                db.Update(obj);
                return true;
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "Archive");
            }
        }
    }
}