using IBeam.DataModels;
using IBeam.Models.API;
using IBeam.Models.Interfaces;
using System;

namespace IBeam.Services.System
{
    public interface IBaseService
    {
        void BeginAudit(IDTO idto, string entityName, bool setNewId = true);
        void EndAudit();
        void SetApplicationId(Guid applicationId);
        void SetId(IDTO idto);
        void SetAccountContext(IAccountContext AccountContext);
        IAccountContext GetAccountContext();
        void IgnoreContext();
    }
}