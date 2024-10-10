using AutoMapper;
using IBeam.DataModels;
using IBeam.Models.API;

namespace IBeam.Services.System
{
    public interface IBaseService<TDTO, IBaseModel> where TDTO : class
    {
        void Delete(IBaseModel model);
        void DeleteById(Guid id);
        List<IBaseModel> GetAll();
        IBaseModel GetById(Guid id);
        List<IBaseModel> GetByIds(List<Guid> ids);
        bool Save(IBaseModel model);
        void SaveAll(List<IBaseModel> models);
        bool Archive(IBaseModel model);
        void ArchiveAll(List<IBaseModel> models);
    
       
        void BeginAudit(IDTO idto, string entityName, bool setNewId = true);
        void EndAudit();
        void SetApplicationId(Guid applicationId);
        void SetId(IDTO idto);
        void SetAccountContext(IAccountContext AccountContext);
        IAccountContext GetAccountContext();
        void IgnoreContext();
    }
}