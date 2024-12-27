using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IBeam.DataModels.System
{
    public class TenantContext
    {
        public Guid? TenantId { get; private set; }

        public TenantContext() { }

        public TenantContext(Guid tenantId)
        {
            TenantId = tenantId;
        }

        public void SetTenantId(Guid tenantId)
        {
            TenantId = tenantId;
        }

        public void ClearTenantId()
        {
            TenantId = null;
        }

        public bool IsTenantIdSet()
        {
            return TenantId.HasValue;
        }
    }
}
