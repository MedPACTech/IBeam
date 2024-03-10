using IBeam.DataModels;
using IBeam.Models;
using IBeam.Repositories.Interfaces;
using IBeam.API.Utilities;
using Microsoft.Extensions.Caching.Memory;
using ServiceStack.OrmLite;
using System.Data;
using Microsoft.Extensions.Options;

namespace IBeam.Repositories
{
    public class DocumentRepository : BaseRepository<DocumentDTO>, IDocumentRepository
    {
        public DocumentRepository(IOptions<AppSettings> appSettings, IMemoryCache memoryCache) : base(appSettings, memoryCache) { }

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

        public bool Archive(Document image)
        {
            try
            {
                using IDbConnection db = _dataFactory.OpenDbConnection();
                var obj = db.Select<DocumentDTO>(x => x.Id == image.Id).FirstOrDefault();
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