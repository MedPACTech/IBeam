using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using IBeam.Repositories.Abstractions;

namespace IBeam.API
{
    public sealed class TenantContext : ITenantContext
    {
        private readonly IHttpContextAccessor _http;

        public TenantContext(IHttpContextAccessor http)
        {
            _http = http;
        }

        public Guid? TenantId
        {
            get
            {
                var user = _http.HttpContext?.User;
                if (user?.Identity?.IsAuthenticated != true)
                    return null;

                var raw =
                    user.FindFirstValue("tenantId")
                    ?? user.FindFirstValue("TenantId")
                    ?? user.FindFirstValue("tid");

                return Guid.TryParse(raw, out var id) ? id : null;
            }
        }

        public bool IsTenantIdSet()
            => TenantId.HasValue && TenantId.Value != Guid.Empty;
    }
}
